// =============================================================
//  ArduinoUnityBridge.ino  —  v3
//  Board : Arduino Uno
//
//  Hardware layout
//  ---------------
//  Object 1  Lamp      Piezo → A0   NeoPixel LED1 → Pin 2
//                               Holy bulb LED strip → Pin 3
//  Object 2  Phone     Piezo → A1   Microswitch   → Pin 8
//                               DFPlayer Mini RX  → Pin 10
//                               DFPlayer Mini TX  → Pin 11
//                               Speaker on DFPlayer SPK_1/SPK_2
//  Object 3  Monitor   Piezo → A2   Computer DFPlayer RX → Pin 12
//                                      Computer DFPlayer TX → Pin 13
//                                      Computer speaker on Computer DFPlayer SPK_1/SPK_2
//  Object 4  Speaker   Piezo → A3
//  Object 5  Printer   Piezo → A4   L298N motor driver
//
//  Printer wiring (L298N)
//  ----------------------
//    IN1 → Pin 4    IN2 → Pin 5   (Motor A)
//    IN3 → Pin 6    IN4 → Pin 7   (Motor B)
//    ENA / ENB      → leave jumper caps on (full speed)
//    12V / GND      → external power supply
//    GND            → shared with Arduino GND
//
//  DFPlayer Mini — SD card folder layout
//  --------------------------------------
//    01/  God's voice
//    02/  Devil's voice
//    03/  Phone hang up audio effect
//
//  Serial protocol
//  ---------------
//  Arduino → Unity   "HIT_1" … "HIT_5"
//                    "PHONE_PICKUP"
//                    "PHONE_PUTDOWN"
//                    "BADGE:SCANNED"   (one-shot; re-armed by SYSTEM:RESET)
//
//  Unity → Arduino   "LED1:NORMAL"   | "LED1:ANOMALY"  | "LED1:BAIT"
//                    "PHONE:NORMAL"  | "PHONE:BAIT"    | "PHONE:ANOMALY"
//                    "PRINTER:ON"    | "PRINTER:OFF"
//                    "MONITOR:ANOMALY"| "MONITOR:NORMAL"
//                    "SYSTEM:RESET"  (also re-arms badge detection)
//
//  Libraries required
//    Adafruit NeoPixel     (Arduino Library Manager)
//    DFRobotDFPlayerMini   (Arduino Library Manager)
// =============================================================

#include <Adafruit_NeoPixel.h>
#include <DFRobotDFPlayerMini.h>
#include <SoftwareSerial.h>
#include <math.h>

// ╔═══════════════════════════════════════════════════════════╗
// ║  CONFIGURATION                                            ║
// ╚═══════════════════════════════════════════════════════════╝

// ---- Serial ------------------------------------------------
const int BAUD_RATE = 9600;

// ---- Piezos (Standard & Phone) -----------------------------
//   Parallel arrays — same index = same object.
//   STD_HIT_NUMBERS maps each slot to its HIT_N number.
const int NUM_STD_PIEZOS = 4;
const int STD_PIEZO_PINS[NUM_STD_PIEZOS] = {A0, A2, A3, A4};
const int STD_HIT_NUMBERS[NUM_STD_PIEZOS] = {1, 3, 4, 5};
// Dynamic spike detection thresholds (Lamp A0, Monitor A2, Speaker A3, Printer
// A4)
const int STD_PIEZO_THRESHOLDS[NUM_STD_PIEZOS] = {150, 150, 150, 150};
const int PHONE_PIEZO_THRESHOLD = 150; // Phone A1
const unsigned long PIEZO_COOLDOWN_MS = 500;

// Index of the Printer within the standard piezo array (A4 = index 3)
const int PRINTER_STD_INDEX = 3;

// ---- Lamp NeoPixel LED1 ------------------------------------
const int NUM_PIXELS = 7;
const int LED1_DATA_PIN = 2;
const int BRIGHTNESS = 90;

const uint8_t COLOR_NORMAL_R = 255, COLOR_NORMAL_G = 0,
              COLOR_NORMAL_B = 0; // red
const uint8_t COLOR_ANOMALY_R = 255, COLOR_ANOMALY_G = 120,
              COLOR_ANOMALY_B = 10; // warm orange-gold
const unsigned long BAIT_FLASH_MS = 300;

// ---- Extra Lamp Holy Bulb Strip ----------------------------
// Additional WS2812B / NeoPixel strip inside the lamp bulb.
const int HOLY_BULB_DATA_PIN = 3;
const int HOLY_BULB_PIXELS = 8;
const int HOLY_BULB_BRIGHTNESS = 255;

// Warm orange-gold used for the inner bulb glow.
const uint8_t BULB_R = 255, BULB_G = 120, BULB_B = 10;
const float BULB_BREATH_SPEED = 8.0; // larger = faster breathing

// ---- Phone hardware ----------------------------------------
const int PHONE_PIEZO_PIN = A1;
const int SWITCH_PIN = 8; // INPUT_PULLUP: LOW = phone picked up

const int DFPLAYER_RX_PIN = 10; // Arduino RX ← DFPlayer TX
const int DFPLAYER_TX_PIN = 11; // Arduino TX → DFPlayer RX
const int DFPLAYER_VOLUME = 30; // 0–30

// ---- Monitor / Computer DFPlayer ---------------------------
// Second DFPlayer dedicated to computer error sound.
const int MONITOR_DFPLAYER_RX_PIN = 12; // Arduino RX ← Monitor DFPlayer TX
const int MONITOR_DFPLAYER_TX_PIN = 13; // Arduino TX → Monitor DFPlayer RX
const int MONITOR_DFPLAYER_VOLUME = 30; // 0–30
const uint8_t MONITOR_AUDIO_FOLDER = 1; // /01/

// Microswitch debounce
const unsigned long SWITCH_DEBOUNCE_MS = 50;

// How long folder 01 plays before switching to folder 02 (ANOMALY only)
const unsigned long FOLDER01_PLAY_MS = 1000;

// ---- Printer motor pins ------------------------------------
const int MOTOR_IN1 = 4, MOTOR_IN2 = 5, MOTOR_IN3 = 6, MOTOR_IN4 = 7;

// ---- Employee Badge (Micro Switch) -------------------------
//   COM → D9   NO → GND   (INPUT_PULLUP; pressed = LOW)
const int BADGE_PIN = 9;

// ╔═══════════════════════════════════════════════════════════╗
// ║  Enumerations                                             ║
// ╚═══════════════════════════════════════════════════════════╝

enum LedState { LED_NORMAL, LED_ANOMALY, LED_BAIT };
enum BulbState { BULB_NORMAL_STATE, BULB_ANOMALY_STATE, BULB_BAIT_STATE };
enum PhoneState { PHONE_IDLE, PHONE_BAIT, PHONE_ANOMALY };
enum PhoneAudio { AUDIO_NONE, AUDIO_FOLDER01, AUDIO_FOLDER02, AUDIO_FOLDER03 };

// ╔═══════════════════════════════════════════════════════════╗
// ║  DynamicPiezo (Auto-adaptive thresholding)                ║
// ╚═══════════════════════════════════════════════════════════╝

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
    // gently adapt baseline to ambient noise, even during a hit
    baseLvl = (baseLvl * 15.0 + val) / 16.0;
    return hit;
  }
};

// ╔═══════════════════════════════════════════════════════════╗
// ║  LedRing  (Lamp only in v3)                               ║
// ╚═══════════════════════════════════════════════════════════╝

class LedRing {
public:
  void begin(Adafruit_NeoPixel *strip) {
    _strip = strip;
    _ledState = LED_NORMAL;
    _strip->setBrightness(BRIGHTNESS);
    _strip->begin();
    setNormal();
  }

  void setNormal() {
    _ledState = LED_NORMAL;
    _flashOn = false;
    _fill(_strip->Color(COLOR_NORMAL_R, COLOR_NORMAL_G, COLOR_NORMAL_B));
  }

  void setAnomaly() {
    _ledState = LED_ANOMALY;
    _startTime = millis();
  }

  void startBait() {
    _ledState = LED_BAIT;
    _flashOn = false; // start dark for the single flash
    _lastFlashTime = millis();
    _strip->clear();
    _strip->show();
  }

  void update() {
    unsigned long now = millis();
    if (_ledState == LED_ANOMALY) {
      // Sine-wave breathing: never fully off (15% floor), ~1 cycle/sec
      float t = (now - _startTime) / 1000.0;
      float breath = (sin(t * 6.0) + 1.0) / 2.0;
      float brightness = 0.15 + breath * 0.85;
      uint8_t r = (uint8_t)(COLOR_ANOMALY_R * brightness);
      uint8_t g = (uint8_t)(COLOR_ANOMALY_G * brightness);
      uint8_t b = (uint8_t)(COLOR_ANOMALY_B * brightness);
      _fill(_strip->Color(r, g, b));
    } else if (_ledState == LED_BAIT) {
      // Single flash: was off, restore red after one blink period
      if (!_flashOn && now - _lastFlashTime >= BAIT_FLASH_MS) {
        _flashOn = true;
        _fill(_strip->Color(COLOR_NORMAL_R, COLOR_NORMAL_G, COLOR_NORMAL_B));
      }
    }
  }

private:
  Adafruit_NeoPixel *_strip;
  LedState _ledState;
  bool _flashOn = false;
  unsigned long _lastFlashTime = 0;
  unsigned long _startTime = 0;

  void _fill(uint32_t color) {
    _strip->fill(color);
    _strip->show();
  }
};

// ╔═══════════════════════════════════════════════════════════╗
// ║  HolyBulbStrip (extra bulb glow inside Lamp)              ║
// ╚═══════════════════════════════════════════════════════════╝

class HolyBulbStrip {
public:
  void begin(Adafruit_NeoPixel *strip) {
    _strip = strip;
    _strip->setBrightness(HOLY_BULB_BRIGHTNESS);
    _strip->begin();
    setNormal();
  }

  void setNormal() {
    _state = BULB_NORMAL_STATE;
    _flashOn = true;
    _fill(_strip->Color(COLOR_NORMAL_R, COLOR_NORMAL_G, COLOR_NORMAL_B));
  }

  void setAnomaly() {
    _state = BULB_ANOMALY_STATE;
    _startTime = millis();
  }

  void startBait() {
    _state = BULB_BAIT_STATE;
    _flashOn = false; // start dark for the single flash
    _lastFlashTime = millis();
    _strip->clear();
    _strip->show();
  }

  void update() {
    unsigned long now = millis();

    if (_state == BULB_ANOMALY_STATE) {
      // Sine-wave breathing: never fully off (15% floor), ~1 cycle/sec
      float t = (now - _startTime) / 1000.0;
      float breath = (sin(t * 6.0) + 1.0) / 2.0;
      float brightness = 0.15 + breath * 0.85;
      uint8_t r = (uint8_t)(COLOR_ANOMALY_R * brightness);
      uint8_t g = (uint8_t)(COLOR_ANOMALY_G * brightness);
      uint8_t b = (uint8_t)(COLOR_ANOMALY_B * brightness);
      _fill(_strip->Color(r, g, b));
      return;
    }

    if (_state == BULB_BAIT_STATE) {
      // Single flash: was off, restore red after one blink period
      if (!_flashOn && now - _lastFlashTime >= BAIT_FLASH_MS) {
        _flashOn = true;
        _fill(_strip->Color(COLOR_NORMAL_R, COLOR_NORMAL_G, COLOR_NORMAL_B));
      }
    }
  }

private:
  Adafruit_NeoPixel *_strip;
  BulbState _state = BULB_NORMAL_STATE;
  bool _flashOn = true;
  unsigned long _lastFlashTime = 0;
  unsigned long _startTime = 0;

  void _fill(uint32_t color) {
    _strip->fill(color);
    _strip->show();
  }
};

// ╔═══════════════════════════════════════════════════════════╗
// ║  Printer motor helpers                                    ║
// ╚═══════════════════════════════════════════════════════════╝

void printerOn() {
  digitalWrite(MOTOR_IN1, HIGH);
  digitalWrite(MOTOR_IN2, LOW);
  digitalWrite(MOTOR_IN3, HIGH);
  digitalWrite(MOTOR_IN4, LOW);
}

void printerOff() {
  digitalWrite(MOTOR_IN1, LOW);
  digitalWrite(MOTOR_IN2, LOW);
  digitalWrite(MOTOR_IN3, LOW);
  digitalWrite(MOTOR_IN4, LOW);
}

// ╔═══════════════════════════════════════════════════════════╗
// ║  Globals                                                  ║
// ╚═══════════════════════════════════════════════════════════╝

// Lamp LED
Adafruit_NeoPixel lampStrip(NUM_PIXELS, LED1_DATA_PIN, NEO_GRB + NEO_KHZ800);
LedRing lampRing;

// Extra lamp bulb LED strip
Adafruit_NeoPixel holyBulbStrip(HOLY_BULB_PIXELS, HOLY_BULB_DATA_PIN, NEO_GRB + NEO_KHZ800);
HolyBulbStrip holyBulb;

// Phone DFPlayer
SoftwareSerial dfSerial(DFPLAYER_RX_PIN, DFPLAYER_TX_PIN);
DFRobotDFPlayerMini dfPlayer;

// Monitor / Computer DFPlayer
// This player is write-only in this sketch, so it does not use DFRobotDFPlayerMini.
SoftwareSerial monitorDfSerial(MONITOR_DFPLAYER_RX_PIN, MONITOR_DFPLAYER_TX_PIN);
bool monitorAudioOn = false;

// ---- SD Card Track Configuration ----
// Specify the exact number of files in each folder here:
const int folder01Count = 1; // Folder '01/': God's voice
const int folder02Count = 3; // Folder '02/': Devil's voice
const int folder03Count = 1; // Folder '03/': Phone hang up audio effect

// Standard piezo cooldowns
unsigned long stdLastHit[NUM_STD_PIEZOS] = {0};
DynamicPiezo stdPiezos[NUM_STD_PIEZOS];
DynamicPiezo phonePiezoTracker;

// Phone state machine
PhoneState phoneState = PHONE_IDLE;
PhoneAudio audioStage = AUDIO_NONE;
bool phoneUp = false;
unsigned long folder01StartTime = 0;

// Microswitch debounce
int rawSwitchState = HIGH;
int stableSwitchState = HIGH;
unsigned long lastSwitchActivity = 0;

// Phone piezo cooldown (independent of microswitch)
unsigned long lastPhonePiezoHit = 0;

// Badge state
bool waitingForBadge = true;   // false after BADGE:SCANNED; re-armed by SYSTEM:RESET
bool lastBadgeState  = HIGH;   // for edge detection (HIGH→LOW = card inserted)

// ╔═══════════════════════════════════════════════════════════╗
// ║  DFPlayer helpers                                         ║
// ╚═══════════════════════════════════════════════════════════╝

void sendMonitorCommand(uint8_t cmd, uint8_t param1, uint8_t param2) {
  uint8_t command[8] = {0x7E, 0xFF, 0x06, cmd, 0x00, param1, param2, 0xEF};
  monitorDfSerial.write(command, 8);
}

void setMonitorVolume(uint8_t volume) {
  if (volume > 30) volume = 30;
  sendMonitorCommand(0x06, 0x00, volume);
}

void startMonitorAudio() {
  // Loop all files in folder 01. Since the monitor SD card only has 01/001.mp3,
  // this behaves like looping the computer error sound until stopped.
  sendMonitorCommand(0x17, 0x00, MONITOR_AUDIO_FOLDER);
  monitorAudioOn = true;
}

void stopMonitorAudio() {
  sendMonitorCommand(0x16, 0x00, 0x00);
  monitorAudioOn = false;
}

// Play a random track from a phone DFPlayer folder using a raw write-only
// command (DFPlayer 0x0F = play file in folder). Never blocks on ACK.
void playRandomFromFolder(int folder, int count) {
  if (count < 1)
    count = 1;
  uint8_t track = (uint8_t)random(1, count + 1);
  sendPhoneRawCommand(0x0F, (uint8_t)folder, track);
}

// Send a raw 8-byte command directly to the phone DFPlayer serial line.
// Write-only — never reads back, never blocks beyond the ~8 ms SoftwareSerial
// transmit time, and does NOT require dfSerial.listen() to be active.
void sendPhoneRawCommand(uint8_t cmd, uint8_t param1, uint8_t param2) {
  uint8_t packet[8] = {0x7E, 0xFF, 0x06, cmd, 0x00, param1, param2, 0xEF};
  dfSerial.write(packet, 8);
}

// Loop all tracks in a folder (DFPlayer command 0x17).
// Self-repeating — no end-of-track callback needed.
void loopPhoneFolder(uint8_t folder) {
  sendPhoneRawCommand(0x17, 0x00, folder);
}

// Stop phone DFPlayer playback using a raw write-only command (0x16 = stop).
// Never blocks on ACK, so the main loop continues running immediately.
void stopAudio() {
  sendPhoneRawCommand(0x16, 0x00, 0x00);
  audioStage = AUDIO_NONE;
}

// ╔═══════════════════════════════════════════════════════════╗
// ║  Badge module                                             ║
// ╚═══════════════════════════════════════════════════════════╝

// Called once per loop(). Non-blocking.
// Detects the HIGH→LOW edge (card insertion) and sends BADGE:SCANNED
// exactly once per session. Badge detection is re-armed by SYSTEM:RESET.
void checkBadge() {
  if (!waitingForBadge) return;

  bool currentState = digitalRead(BADGE_PIN);

  // Edge detection: only fire on HIGH → LOW transition
  if (currentState == LOW && lastBadgeState == HIGH) {
    waitingForBadge = false;
    Serial.println(F("BADGE:SCANNED"));
  }

  lastBadgeState = currentState;
}

// ╔═══════════════════════════════════════════════════════════╗
// ║  Command parser                                           ║
// ╚═══════════════════════════════════════════════════════════╝

void handleCommand(const String &cmd) {

  // ---- System Reset ---------------------------------------
  if (cmd == "SYSTEM:RESET") {
    printerOff();
    lampRing.setNormal();
    holyBulb.setNormal();
    stopAudio();
    stopMonitorAudio();
    phoneState = PHONE_IDLE;
    lastPhonePiezoHit = 0;
    // Re-arm badge detection for the next game session
    waitingForBadge = true;
    lastBadgeState  = HIGH;
    return;
  }

  // ---- Printer --------------------------------------------
  if (cmd == "PRINTER:ON") {
    printerOn();
    return;
  }
  if (cmd == "PRINTER:OFF") {
    printerOff();
    return;
  }

  // ---- Phone state ----------------------------------------
  if (cmd == "PHONE:NORMAL") {
    phoneState = PHONE_IDLE;
    stopAudio();
    return;
  }
  if (cmd == "PHONE:BAIT") {
    phoneState = PHONE_BAIT;
    // If phone is already in-hand when state arrives, start audio immediately
    if (phoneUp) {
      playRandomFromFolder(2, folder02Count);
      audioStage = AUDIO_FOLDER02;
    }
    return;
  }
  if (cmd == "PHONE:ANOMALY") {
    phoneState = PHONE_ANOMALY;
    if (phoneUp) {
      loopPhoneFolder(1);
      audioStage = AUDIO_FOLDER01;
    }
    return;
  }

  // ---- Lamp LED -------------------------------------------
  int colonIdx = cmd.indexOf(':');
  if (colonIdx == -1)
    return;

  String target = cmd.substring(0, colonIdx);
  String command = cmd.substring(colonIdx + 1);

  if (target == "LED1") {
    if (command == "NORMAL") {
      lampRing.setNormal();
      holyBulb.setNormal();
    } else if (command == "ANOMALY") {
      lampRing.setAnomaly();
      holyBulb.setAnomaly();
    } else if (command == "BAIT") {
      lampRing.startBait();
      holyBulb.startBait();
    }
    return;
  }

  // ---- Monitor / Computer audio ---------------------------
  if (target == "MONITOR") {
    if (command == "ANOMALY") {
      startMonitorAudio();
    } else if (command == "NORMAL") {
      stopMonitorAudio();
    }
    return;
  }
}

// ╔═══════════════════════════════════════════════════════════╗
// ║  Setup                                                    ║
// ╚═══════════════════════════════════════════════════════════╝

void setup() {
  Serial.begin(BAUD_RATE);

  // A5 is unconnected now — floating pin makes a good noise source
  randomSeed(analogRead(A5));

  // ---- Piezos initialization ------------------------------
  for (int i = 0; i < NUM_STD_PIEZOS; i++) {
    stdPiezos[i].begin(STD_PIEZO_PINS[i], STD_PIEZO_THRESHOLDS[i]);
  }
  phonePiezoTracker.begin(PHONE_PIEZO_PIN, PHONE_PIEZO_THRESHOLD);

  // ---- Lamp LED -------------------------------------------
  lampRing.begin(&lampStrip);
  holyBulb.begin(&holyBulbStrip);

  // ---- Printer motor pins ---------------------------------
  pinMode(MOTOR_IN1, OUTPUT);
  pinMode(MOTOR_IN2, OUTPUT);
  pinMode(MOTOR_IN3, OUTPUT);
  pinMode(MOTOR_IN4, OUTPUT);
  printerOff();

  // ---- Phone microswitch ----------------------------------
  pinMode(SWITCH_PIN, INPUT_PULLUP);

  // ---- Employee Badge microswitch -------------------------
  pinMode(BADGE_PIN, INPUT_PULLUP);

  // ---- DFPlayer -------------------------------------------
  dfSerial.begin(9600);
  monitorDfSerial.begin(9600);
  delay(1000); // allow modules to boot

  // Activate dfSerial as the SoftwareSerial listener so dfPlayer.begin()
  // can receive the DFPlayer module's init-ready byte.
  // After begin() returns, dfSerial.listen() is never called again:
  // all subsequent phone DFPlayer operations use raw write-only commands
  // (sendPhoneRawCommand) that never block on ACK, keeping the main loop
  // free to run piezo detection and microswitch debounce continuously.
  dfSerial.listen();
  bool phonePlayerReady = dfPlayer.begin(dfSerial, false); // isACK=false — no ACK waits after init
  if (!phonePlayerReady) {
    Serial.println(F("[DFPlayer] Init FAILED — check wiring and SD card."));
  } else {
    Serial.print(F("[DFPlayer] Ready. Using hardcoded folder sizes: 01 ("));
    Serial.print(folder01Count);
    Serial.print(F(" tracks) / 02 ("));
    Serial.print(folder02Count);
    Serial.print(F(" tracks) / 03 ("));
    Serial.print(folder03Count);
    Serial.println(F(" tracks)"));
  }
  // Always set volume via raw command regardless of init success.
  // Raw write is ~8 ms of SoftwareSerial bit-bang — acceptable at startup.
  sendPhoneRawCommand(0x06, 0x00, DFPLAYER_VOLUME);

  // ---- Monitor / Computer DFPlayer ------------------------
  setMonitorVolume(MONITOR_DFPLAYER_VOLUME);
  stopMonitorAudio();
  Serial.println(F("[Monitor DFPlayer] Ready on pins 12/13. Using /01/001.mp3."));

  Serial.println(F("READY"));
}

// ╔═══════════════════════════════════════════════════════════╗
// ║  Loop                                                     ║
// ╚═══════════════════════════════════════════════════════════╝

void loop() {
  unsigned long now = millis();

  // ── Badge (one-shot; re-armed by SYSTEM:RESET) ───────────
  checkBadge();

  // ── Lamp LED flash animation ─────────────────────────────
  lampRing.update();
  holyBulb.update();

  // Phone DFPlayer runs in write-only mode — no dfSerial.listen(), no
  // dfPlayer.available() reads. Reading DFPlayer responses via SoftwareSerial
  // caused interrupt-driven blocking that disrupted switch debounce timing,
  // causing PHONE_PUTDOWN to be missed and freezing all piezo detection.
  // Folder01 (ANOMALY) now uses the DFPlayer's built-in loop-folder command
  // (0x17) so it repeats automatically without any end-of-track callback.

  // ── Incoming serial commands from Unity ──────────────────
  if (Serial.available() > 0) {
    String cmd = Serial.readStringUntil('\n');
    cmd.trim();
    handleCommand(cmd);
  }

  // ── Standard piezo hits (Lamp, Monitor, Speaker, Printer)
  for (int i = 0; i < NUM_STD_PIEZOS; i++) {
    bool hitDetected = stdPiezos[i].update();
    if (now - stdLastHit[i] < PIEZO_COOLDOWN_MS)
      continue;
    if (hitDetected) {
      stdLastHit[i] = now;
      Serial.println("HIT_" + String(STD_HIT_NUMBERS[i]));
    }
  }

  // ── Phone microswitch — pickup / putdown detection ───────
  //   Purely for DFPlayer audio (bonus feature). Does NOT gate HIT_2.
  int rawSwitch = digitalRead(SWITCH_PIN);
  if (rawSwitch != rawSwitchState) {
    rawSwitchState = rawSwitch;
    lastSwitchActivity = now;
  }

  if ((now - lastSwitchActivity) >= SWITCH_DEBOUNCE_MS &&
      rawSwitchState != stableSwitchState) {
    stableSwitchState = rawSwitchState;

    if (stableSwitchState == LOW && !phoneUp) {
      // ---- Phone picked up — play audio if Broke/Bait -----
      phoneUp = true;
      Serial.println(F("PHONE_PICKUP"));
      if (phoneState == PHONE_ANOMALY) {
        // Use built-in loop-folder (0x17) so folder01 repeats automatically
        // without needing end-of-track callbacks in the main loop.
        loopPhoneFolder(1);
        audioStage = AUDIO_FOLDER01;
      } else if (phoneState == PHONE_BAIT) {
        playRandomFromFolder(2, folder02Count);
        audioStage = AUDIO_FOLDER02;
      }

    } else if (stableSwitchState == HIGH && phoneUp) {
      // ---- Phone put down — stop audio --------------------
      phoneUp = false;
      Serial.println(F("PHONE_PUTDOWN"));
      stopAudio();
    }
  }

  // ── Phone piezo hit detection — same logic as standard piezos ──
  //   Completely independent of microswitch state.
  //   Pickup / putdown never blocks or delays HIT_2.
  bool phoneHitThisFrame = phonePiezoTracker.update();
  if ((now - lastPhonePiezoHit) >= PIEZO_COOLDOWN_MS && phoneHitThisFrame) {
    lastPhonePiezoHit = now;
    Serial.println(F("HIT_2"));
    // Bonus: if phone is off-hook during anomaly, play confirmation audio
    if (phoneState == PHONE_ANOMALY && phoneUp && audioStage != AUDIO_FOLDER03) {
      playRandomFromFolder(3, folder03Count);
      audioStage = AUDIO_FOLDER03;
    }
  }
}