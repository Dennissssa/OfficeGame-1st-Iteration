// =============================================================
//  HardwareTest.ino
//  Standalone Diagnostic Sketch (No Game Logic)
// =============================================================
//  Open the Arduino Serial Monitor at 9600 baud.
//  Type these commands to test outputs:
//    'm' or 'M' -> Toggle Motor ON/OFF
//
//    PHONE DFPlayer on RX=10, TX=11:
//    'p <folder> <track>' -> Play phone audio from a specific folder and track
//    'p' or 'P'           -> Play phone audio folder 1, track 1
//    's' or 'S'           -> Stop phone audio
//
//    MONITOR / COMPUTER DFPlayer on RX=12, TX=13:
//    'c' or 'C' -> Start monitor/computer audio loop from /01/001.mp3
//    'x' or 'X' -> Stop monitor/computer audio
//
//    LAMP LEDs:
//    'l' or 'L' -> Cycle LED modes:
//                  Off -> Red -> Green -> Blue -> Orange Gold -> Breathing Orange Gold
//
//  Inputs (Piezos and Microswitch) are monitored constantly
//  and will automatically print when triggered.
// =============================================================

#include <Adafruit_NeoPixel.h>
#include <SoftwareSerial.h>
#include <DFRobotDFPlayerMini.h>
#include <math.h>

// ---- Serial ------------------------------------------------
const int BAUD_RATE = 9600;

// ---- Piezos ------------------------------------------------
const int NUM_PIEZOS = 5;
const int PIEZO_PINS[NUM_PIEZOS] = { A0, A1, A2, A3, A4 };
const String PIEZO_NAMES[NUM_PIEZOS] = {
  "Skull / Lamp (A0)",
  "Phone (A1)",
  "Monitor / Computer (A2)",
  "Speaker (A3)",
  "Printer (A4)"
};

const int PIEZO_THRESHOLDS[NUM_PIEZOS] = { 150, 150, 150, 50, 150 };
const unsigned long PIEZO_COOLDOWN_MS = 500;
unsigned long stdLastHit[NUM_PIEZOS] = { 0 };

class DynamicPiezo {
public:
  int pin;
  float baseLvl = 0;
  int spikeThreshold = 150;

  void begin(int p, int threshold = 150) {
    pin = p;
    spikeThreshold = threshold;
    for (int i = 0; i < 50; i++) {
      baseLvl = (baseLvl * 9.0 + analogRead(pin)) / 10.0;
      delay(1);
    }
  }

  bool update() {
    int val = analogRead(pin);
    bool hit = (val - baseLvl > spikeThreshold);
    baseLvl = (baseLvl * 15.0 + val) / 16.0;
    return hit;
  }
};

DynamicPiezo testPiezos[NUM_PIEZOS];

// ---- Lamp NeoPixel LEDs ------------------------------------
// Old lamp ring
const int RING_PIXELS = 7;
const int RING_DATA_PIN = 2;

// New bulb strip
const int BULB_PIXELS = 8;
const int BULB_DATA_PIN = 3;

const int BRIGHTNESS = 80;
Adafruit_NeoPixel lampRing(RING_PIXELS, RING_DATA_PIN, NEO_GRB + NEO_KHZ800);
Adafruit_NeoPixel bulbStrip(BULB_PIXELS, BULB_DATA_PIN, NEO_GRB + NEO_KHZ800);

int currentLedMode = 0;
// 0=Off, 1=Red, 2=Green, 3=Blue, 4=Orange Gold, 5=Breathing Orange Gold
bool bulbBreathing = false;
unsigned long breathStartTime = 0;

const uint8_t GOLD_R = 255;
const uint8_t GOLD_G = 120;
const uint8_t GOLD_B = 10;

// ---- Phone hardware ----------------------------------------
const int SWITCH_PIN = 8;   // INPUT_PULLUP
int lastSwitchState = HIGH;

// ---- Phone DFPlayer ----------------------------------------
const int PHONE_DFPLAYER_RX_PIN = 10; // Arduino RX ← Phone DFPlayer TX
const int PHONE_DFPLAYER_TX_PIN = 11; // Arduino TX → Phone DFPlayer RX
SoftwareSerial phoneDfSerial(PHONE_DFPLAYER_RX_PIN, PHONE_DFPLAYER_TX_PIN);
DFRobotDFPlayerMini phoneDfPlayer;
bool phoneDfPlayerReady = false;

// ---- Monitor / Computer DFPlayer ---------------------------
const int MONITOR_DFPLAYER_RX_PIN = 12; // Arduino RX ← Monitor DFPlayer TX
const int MONITOR_DFPLAYER_TX_PIN = 13; // Arduino TX → Monitor DFPlayer RX
SoftwareSerial monitorDfSerial(MONITOR_DFPLAYER_RX_PIN, MONITOR_DFPLAYER_TX_PIN);
DFRobotDFPlayerMini monitorDfPlayer;
bool monitorDfPlayerReady = false;

// ---- SD Card Configuration for phone DFPlayer ---------------
const int NUM_FOLDERS = 4;
const int FOLDER_FILE_COUNTS[] = { 4, 6, 2, 1 };
// Folder 01: God's voice
// Folder 02: Devil's voice
// Folder 03: Devil closing
// Folder 04: Phone hang up audio effect

// Monitor / Computer SD card:
// /01/001.mp3 = computer error / warning sound

// ---- Printer motor pins ------------------------------------
const int MOTOR_IN1 = 4, MOTOR_IN2 = 5, MOTOR_IN3 = 6, MOTOR_IN4 = 7;
bool motorOn = false;

// ---- Combo Testing State -----------------------------------
int testMode = 0; // 0=NORMAL, 1=BAIT, 2=HACK
bool phoneTestUp = false;
int testAudioStage = 0; // 0=NONE, 1=FOLDER01, 2=FOLDER02, 4=FOLDER04

// ╔═══════════════════════════════════════════════════════════╗
// ║  LED helpers                                              ║
// ╚═══════════════════════════════════════════════════════════╝

void setBothLampLights(uint8_t r, uint8_t g, uint8_t b) {
  uint32_t ringColor = lampRing.Color(r, g, b);
  uint32_t stripColor = bulbStrip.Color(r, g, b);
  lampRing.fill(ringColor);
  bulbStrip.fill(stripColor);
  lampRing.show();
  bulbStrip.show();
}

void updateBreathingBulb() {
  if (!bulbBreathing) return;

  float t = (millis() - breathStartTime) / 1000.0;
  float breath = (sin(t * 8.0) + 1.0) / 2.0; // fast holy-light breathing
  float brightness = 0.15 + breath * 0.85;   // never fully black

  uint8_t r = (uint8_t)(GOLD_R * brightness);
  uint8_t g = (uint8_t)(GOLD_G * brightness);
  uint8_t b = (uint8_t)(GOLD_B * brightness);

  bulbStrip.fill(bulbStrip.Color(r, g, b));
  bulbStrip.show();
}

void cycleLedMode() {
  currentLedMode = (currentLedMode + 1) % 6;
  bulbBreathing = false;

  Serial.print(F("> LED Mode switched to: "));

  if (currentLedMode == 0) {
    Serial.println(F("OFF"));
    setBothLampLights(0, 0, 0);
  } else if (currentLedMode == 1) {
    Serial.println(F("RED"));
    setBothLampLights(255, 0, 0);
  } else if (currentLedMode == 2) {
    Serial.println(F("GREEN"));
    setBothLampLights(0, 255, 0);
  } else if (currentLedMode == 3) {
    Serial.println(F("BLUE"));
    setBothLampLights(0, 0, 255);
  } else if (currentLedMode == 4) {
    Serial.println(F("ORANGE GOLD"));
    setBothLampLights(GOLD_R, GOLD_G, GOLD_B);
  } else if (currentLedMode == 5) {
    Serial.println(F("BREATHING ORANGE GOLD"));
    // Old ring stays solid orange gold.
    lampRing.fill(lampRing.Color(GOLD_R, GOLD_G, GOLD_B));
    lampRing.show();

    bulbBreathing = true;
    breathStartTime = millis();
  }
}

// ╔═══════════════════════════════════════════════════════════╗
// ║  Setup                                                    ║
// ╚═══════════════════════════════════════════════════════════╝
void setup() {
  Serial.begin(BAUD_RATE);
  Serial.println(F("========================================="));
  Serial.println(F("  HARDWARE DIAGNOSTIC TEST STARTING... "));
  Serial.println(F("========================================="));

  // ---- Piezos initialization ------------------------------
  Serial.print(F("Calibrating Piezos... "));
  for (int i = 0; i < NUM_PIEZOS; i++) {
    testPiezos[i].begin(PIEZO_PINS[i], PIEZO_THRESHOLDS[i]);
  }
  Serial.println(F("[OK]"));

  // ---- Lamp LEDs ------------------------------------------
  lampRing.setBrightness(BRIGHTNESS);
  bulbStrip.setBrightness(120);
  lampRing.begin();
  bulbStrip.begin();
  lampRing.show();
  bulbStrip.show();
  Serial.println(F("[OK] Lamp NeoPixels Initialized: Ring=D2, Bulb Strip=D3"));

  // ---- Printer motor --------------------------------------
  pinMode(MOTOR_IN1, OUTPUT); pinMode(MOTOR_IN2, OUTPUT);
  pinMode(MOTOR_IN3, OUTPUT); pinMode(MOTOR_IN4, OUTPUT);
  digitalWrite(MOTOR_IN1, LOW); digitalWrite(MOTOR_IN2, LOW);
  digitalWrite(MOTOR_IN3, LOW); digitalWrite(MOTOR_IN4, LOW);
  Serial.println(F("[OK] Motor Pins Set"));

  // ---- Phone microswitch ----------------------------------
  pinMode(SWITCH_PIN, INPUT_PULLUP);
  lastSwitchState = digitalRead(SWITCH_PIN);
  Serial.println(F("[OK] Microswitch Set (Pin 8)"));

  // ---- Phone DFPlayer -------------------------------------
  Serial.println(F("Booting PHONE DFPlayer Module on pins RX=10, TX=11..."));
  phoneDfSerial.begin(9600);
  delay(1000);
  phoneDfSerial.listen();
  if (!phoneDfPlayer.begin(phoneDfSerial)) {
    Serial.println(F("[!] PHONE DFPlayer Init FAILED. Check wiring/SD card."));
  } else {
    phoneDfPlayerReady = true;
    phoneDfPlayer.volume(20);
    Serial.println(F("[OK] PHONE DFPlayer initialized."));
  }

  delay(300);

  // ---- Monitor DFPlayer -----------------------------------
  Serial.println(F("Booting MONITOR DFPlayer Module on pins RX=12, TX=13..."));
  monitorDfSerial.begin(9600);
  delay(1000);
  monitorDfSerial.listen();
  if (!monitorDfPlayer.begin(monitorDfSerial)) {
    Serial.println(F("[!] MONITOR DFPlayer Init FAILED. Check wiring/SD card."));
  } else {
    monitorDfPlayerReady = true;
    monitorDfPlayer.volume(25);
    Serial.println(F("[OK] MONITOR DFPlayer initialized."));
  }

  // Return listening focus to phone DFPlayer, because only the phone player is used for end-of-track feedback.
  phoneDfSerial.listen();

  Serial.print(F("\n--- Phone SD Card Info (HARDCODED) ---\nFolders defined: "));
  Serial.println(NUM_FOLDERS);
  for (int i = 0; i < NUM_FOLDERS; i++) {
    Serial.print(F("  Folder "));
    if (i + 1 < 10) Serial.print(F("0"));
    Serial.print(i + 1);
    Serial.print(F(" -> Files: "));
    Serial.println(FOLDER_FILE_COUNTS[i]);
  }
  Serial.println(F("Monitor SD card expected: /01/001.mp3"));
  Serial.println(F("--------------------\n"));

  Serial.println(F("--- TEST COMMANDS OVER SERIAL ---"));
  Serial.println(F(" Type 'm' and press enter to toggle MOTOR."));
  Serial.println(F(" Type 'p <folder> <track>' to PLAY PHONE audio."));
  Serial.println(F(" Type 'p' by itself to PLAY PHONE folder 1, track 1."));
  Serial.println(F(" Type 's' and press enter to STOP PHONE audio."));
  Serial.println(F(" Type 'c' and press enter to START MONITOR computer error loop (/01/001.mp3)."));
  Serial.println(F(" Type 'x' and press enter to STOP MONITOR audio."));
  Serial.println(F(" Type 'l' and press enter to cycle Ring + Bulb LED modes."));
  Serial.println(F("\n--- COMBO BEHAVIOR TESTING ---"));
  Serial.println(F(" Type 'b' to enter BAIT mode (Plays Phone Folder 02 on pickup)."));
  Serial.println(F(" Type 'h' to enter HACK mode (Plays Phone Folder 01 on pickup)."));
  Serial.println(F(" Type 'n' to return to NORMAL mode (No auto playback)."));
  Serial.println(F("\nListening for piezos and switch changes..."));
  Serial.println(F("========================================="));
}

// ╔═══════════════════════════════════════════════════════════╗
// ║  Loop                                                     ║
// ╚═══════════════════════════════════════════════════════════╝
void loop() {
  unsigned long now = millis();

  updateBreathingBulb();

  // 0. Auto Chaining for Phone Combo test
  phoneDfSerial.listen();
  if (phoneDfPlayerReady && phoneDfPlayer.available()) {
    uint8_t type = phoneDfPlayer.readType();
    int value = phoneDfPlayer.read(); // Consume the value
    if (type == DFPlayerPlayFinished) {
      if (phoneTestUp) {
        if (testAudioStage == 1) {
          phoneDfPlayer.playFolder(1, random(1, 3));
          testAudioStage = 1;
        } else if (testAudioStage == 2) {
          int trackQty = FOLDER_FILE_COUNTS[3];
          if (trackQty < 1) trackQty = 1;
          phoneDfPlayer.playFolder(4, random(1, trackQty + 1));
          testAudioStage = 4;
        }
      }
    }
  }

  // 1. Check Serial Commands
  if (Serial.available() > 0) {
    String cmdStr = Serial.readStringUntil('\n');
    cmdStr.trim();
    if (cmdStr.length() == 0) return;

    char cmd = cmdStr.charAt(0);

    if (cmd == 'm' || cmd == 'M') {
      motorOn = !motorOn;
      if (motorOn) {
        Serial.println(F("> Motor turned ON"));
        digitalWrite(MOTOR_IN1, HIGH); digitalWrite(MOTOR_IN2, LOW);
        digitalWrite(MOTOR_IN3, HIGH); digitalWrite(MOTOR_IN4, LOW);
      } else {
        Serial.println(F("> Motor turned OFF"));
        digitalWrite(MOTOR_IN1, LOW); digitalWrite(MOTOR_IN2, LOW);
        digitalWrite(MOTOR_IN3, LOW); digitalWrite(MOTOR_IN4, LOW);
      }
    }

    if (cmd == 'p' || cmd == 'P') {
      phoneDfSerial.listen();
      if (phoneDfPlayerReady) {
        int space1 = cmdStr.indexOf(' ');
        if (space1 > 0) {
          int space2 = cmdStr.indexOf(' ', space1 + 1);
          if (space2 > 0) {
            int folder = cmdStr.substring(space1 + 1, space2).toInt();
            int track = cmdStr.substring(space2 + 1).toInt();
            Serial.print(F("> PHONE DFPlayer play command (Folder "));
            Serial.print(folder);
            Serial.print(F(", Track "));
            Serial.print(track);
            Serial.println(F(")..."));
            phoneDfPlayer.playFolder(folder, track);
          } else {
            Serial.println(F("[!] Invalid format. Use 'p <folder> <track>' (e.g., 'p 1 2')."));
          }
        } else {
          Serial.println(F("> PHONE DFPlayer play command (Folder 1, Track 1)..."));
          phoneDfPlayer.playFolder(1, 1);
        }
      } else {
        Serial.println(F("[!] Cannot play PHONE audio. Phone DFPlayer not ready."));
      }
    }

    if (cmd == 's' || cmd == 'S') {
      phoneDfSerial.listen();
      if (phoneDfPlayerReady) {
        Serial.println(F("> Stopping PHONE DFPlayer..."));
        phoneDfPlayer.stop();
        testAudioStage = 0;
      }
    }

    if (cmd == 'c' || cmd == 'C') {
      monitorDfSerial.listen();
      if (monitorDfPlayerReady) {
        Serial.println(F("> MONITOR DFPlayer: starting loop folder 01 (/01/001.mp3)."));
        // Folder 01 contains only 001.mp3, so looping the folder loops the computer error sound.
        monitorDfPlayer.loopFolder(1);
      } else {
        Serial.println(F("[!] Cannot play MONITOR audio. Monitor DFPlayer not ready."));
      }
      phoneDfSerial.listen();
    }

    if (cmd == 'x' || cmd == 'X') {
      monitorDfSerial.listen();
      if (monitorDfPlayerReady) {
        Serial.println(F("> Stopping MONITOR DFPlayer..."));
        monitorDfPlayer.stop();
      }
      phoneDfSerial.listen();
    }

    if (cmd == 'b' || cmd == 'B') {
      testMode = 1;
      Serial.println(F("> Phone Testing Mode: BAIT (Will play folder 02 on pickup)"));
    }

    if (cmd == 'h' || cmd == 'H') {
      testMode = 2;
      Serial.println(F("> Phone Testing Mode: HACK/ANOMALY (Will play folder 01 on pickup)"));
    }

    if (cmd == 'n' || cmd == 'N') {
      testMode = 0;
      Serial.println(F("> Phone Testing Mode: NORMAL (Will NOT auto-play on pickup)"));
    }

    if (cmd == 'l' || cmd == 'L') {
      cycleLedMode();
    }
  }

  // 2. Check Piezos
  for (int i = 0; i < NUM_PIEZOS; i++) {
    bool hitDetected = testPiezos[i].update();
    if (now - stdLastHit[i] < PIEZO_COOLDOWN_MS) continue;

    if (hitDetected) {
      stdLastHit[i] = now;
      Serial.print(F(">>> PIEZO HIT DETECTED: "));
      Serial.print(PIEZO_NAMES[i]);
      Serial.println(F("  (Dynamic Spike)"));

      // If phone piezo (A1) is hit, stop phone audio
      if (PIEZO_PINS[i] == A1 && phoneDfPlayerReady) {
        phoneDfSerial.listen();
        Serial.println(F("    [Phone Hit] -> Stopping PHONE DFPlayer"));
        phoneDfPlayer.stop();
        testAudioStage = 0;
      }
    }
  }

  // 3. Check Microswitch
  int swState = digitalRead(SWITCH_PIN);
  if (swState != lastSwitchState) {
    if (swState == LOW) {
      Serial.println(F(">>> SW: Phone PICKED UP (Pin 8 went LOW)"));
      phoneTestUp = true;
      phoneDfSerial.listen();
      if (testMode == 1 && phoneDfPlayerReady) {
        int trackQty = FOLDER_FILE_COUNTS[1];
        if (trackQty < 1) trackQty = 1;
        Serial.println(F("    [BAIT Trigger] -> Playing PHONE Folder 02"));
        phoneDfPlayer.playFolder(2, random(1, trackQty + 1));
        testAudioStage = 2;
      } else if (testMode == 2 && phoneDfPlayerReady) {
        Serial.println(F("    [HACK Trigger] -> Playing PHONE Folder 01 loops"));
        phoneDfPlayer.playFolder(1, random(1, 3));
        testAudioStage = 1;
      }
    } else {
      Serial.println(F(">>> SW: Phone PUT DOWN (Pin 8 went HIGH)"));
      phoneTestUp = false;
      phoneDfSerial.listen();
      if (phoneDfPlayerReady) {
        phoneDfPlayer.stop();
        testAudioStage = 0;
      }
    }
    lastSwitchState = swState;
    delay(50); // basic debounce
  }
}
