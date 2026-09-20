#ifndef CRT_TELEVISION_INCLUDED
#define CRT_TELEVISION_INCLUDED

#ifndef SAMPLE_CRT
#error SAMPLE_CRT(uv) must be defined before including CRTTelevision.hlsl
#endif

float4 _CRTPacked0; // intensity, edgeCurl, curlX, curlY
float4 _CRTPacked1; // cornerPinch, overscan, bezelRound, bezelThick
float4 _CRTPacked2; // bezelSoft, scanlineInt, scanlineCount, scanlineSharp
float4 _CRTPacked3; // maskInt, maskScale, maskType, chromatic
float4 _CRTPacked4; // chromaEdge, vignetteInt, vignetteSmooth, brightness
float4 _CRTPacked5; // contrast, saturation, glow, flicker
float4 _CRTPacked6; // noise, wobble, rollBar, time
float4 _CRTPacked7; // aspect, unused, unused, unused
float4 _CRTFlags0;  // scanlines, mask, chromatic, vignette
float4 _CRTFlags1;  // flicker, noise, aspectCorrect, unused
float4 _CRTBezelColor;
float4 _CRTPhosphorTint;
float4 _CRTScreenParams; // w, h, 1/w, 1/h

float CRTHash(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123);
}

float2 CRTWarp(float2 uv)
{
    float edgeCurl = _CRTPacked0.y;
    float curlX = _CRTPacked0.z;
    float curlY = _CRTPacked0.w;
    float cornerPinch = _CRTPacked1.x;
    float overscan = _CRTPacked1.y;
    float aspect = max(_CRTPacked7.x, 0.0001);
    bool aspectCorrect = _CRTFlags1.z > 0.5;

    float2 p = uv * 2.0 - 1.0;
    if (aspectCorrect)
        p.x *= aspect;

    float cx = max(edgeCurl * curlX, 0.0) * 0.42;
    float cy = max(edgeCurl * curlY, 0.0) * 0.42;
    p.x *= 1.0 + p.y * p.y * cx;
    p.y *= 1.0 + p.x * p.x * cy;

    float corner = abs(p.x * p.y) * max(cornerPinch, 0.0) * 0.55;
    p *= 1.0 + corner;

    if (aspectCorrect)
        p.x /= aspect;

    p *= 1.0 - saturate(overscan);
    return p * 0.5 + 0.5;
}

float CRTBezelMask(float2 uv)
{
    float roundness = _CRTPacked1.z;
    float thickness = _CRTPacked1.w;
    float softness = max(_CRTPacked2.x, 0.0001);

    float2 p = uv * 2.0 - 1.0;
    float2 q = abs(p) - (1.0 - thickness - roundness);
    float sd = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - roundness;
    return 1.0 - smoothstep(0.0, softness, sd);
}

float3 CRTPhosphorMask(float2 uv)
{
    float type = _CRTPacked3.z;
    float scale = max(_CRTPacked3.y, 0.6);
    float2 pixel = uv * _CRTScreenParams.xy / scale;

    float3 mask = 1.0;
    if (type < 0.5)
    {
        float f = frac(pixel.x / 3.0);
        mask = f < 0.333 ? float3(1.15, 0.18, 0.18) :
               f < 0.666 ? float3(0.18, 1.15, 0.18) :
                           float3(0.18, 0.18, 1.2);
    }
    else if (type < 1.5)
    {
        float mx = floor(fmod(pixel.x, 3.0));
        float2 cell = frac(pixel * float2(0.5, 0.33));
        float hole = smoothstep(0.42, 0.18, length(cell - 0.5));
        mask = mx < 0.5 ? float3(1.2, 0.16, 0.16) :
               mx < 1.5 ? float3(0.16, 1.2, 0.16) :
                          float3(0.16, 0.16, 1.25);
        mask *= lerp(0.35, 1.0, hole);
    }
    else
    {
        float mx = floor(fmod(pixel.x, 3.0));
        float slot = abs(frac(pixel.y * 0.5) - 0.5);
        mask = mx < 0.5 ? float3(1.18, 0.2, 0.2) :
               mx < 1.5 ? float3(0.2, 1.18, 0.2) :
                          float3(0.2, 0.2, 1.22);
        mask *= lerp(0.55, 1.0, smoothstep(0.42, 0.12, slot));
    }

    return mask;
}

float4 CRTFragment(float2 rawUv)
{
    float intensity = saturate(_CRTPacked0.x);
    float time = _CRTPacked6.w;
    float2 uv = rawUv;
    uv.x += sin(rawUv.y * 48.0 + time * 7.3) * _CRTPacked6.y;

    float2 warped = CRTWarp(uv);
    float bezel = CRTBezelMask(warped);
    bool outside = warped.x < 0.0 || warped.x > 1.0 || warped.y < 0.0 || warped.y > 1.0;

    float2 centerDir = warped - 0.5;
    float dist2 = dot(centerDir, centerDir);

    float chroma = 0.0;
    if (_CRTFlags0.z > 0.5)
        chroma = _CRTPacked3.w * intensity * (1.0 + _CRTPacked4.x * dist2 * 4.0);

    float4 col;
    if (outside)
    {
        col = float4(_CRTBezelColor.rgb, 1.0);
    }
    else
    {
        float r = SAMPLE_CRT(warped + centerDir * chroma).r;
        float g = SAMPLE_CRT(warped).g;
        float b = SAMPLE_CRT(warped - centerDir * chroma).b;
        col = float4(r, g, b, 1.0);
        col.rgb *= 1.0 + _CRTPacked5.z * 0.4 * intensity;
    }

    if (_CRTFlags0.x > 0.5 && !outside)
    {
        float scan = sin(warped.y * 3.14159265 * _CRTPacked2.z);
        scan = pow(abs(scan), max(_CRTPacked2.w, 0.2));
        col.rgb *= lerp(1.0, scan * 0.72 + 0.28, _CRTPacked2.y * intensity);
    }

    if (_CRTFlags0.y > 0.5 && _CRTPacked3.x > 0.001 && !outside)
    {
        float3 mask = CRTPhosphorMask(warped);
        col.rgb *= lerp(1.0, mask, _CRTPacked3.x * intensity);
    }

    if (_CRTFlags0.w > 0.5 && _CRTPacked4.y > 0.001 && !outside)
    {
        float vig = saturate(1.0 - dist2 / max(_CRTPacked4.z, 0.05));
        col.rgb *= lerp(1.0 - _CRTPacked4.y * intensity, 1.0, vig);
    }

    col.rgb = (col.rgb - 0.5) * _CRTPacked5.x + 0.5 + _CRTPacked4.w;
    float luma = dot(col.rgb, float3(0.2126, 0.7152, 0.0722));
    col.rgb = lerp(luma.xxx, col.rgb, _CRTPacked5.y);
    col.rgb *= _CRTPhosphorTint.rgb;

    if (_CRTFlags1.x > 0.5 && _CRTPacked5.w > 0.001)
    {
        float flick = 1.0 + (_CRTPacked5.w * intensity * (sin(time * 62.0) * 0.45 + CRTHash(float2(time, 0.37)) * 0.55 - 0.3));
        col.rgb *= flick;
    }

    if (_CRTFlags1.y > 0.5 && _CRTPacked6.x > 0.001 && !outside)
    {
        float n = CRTHash(warped * _CRTScreenParams.xy + time * 12.0);
        col.rgb += (n - 0.5) * _CRTPacked6.x * intensity;
    }

    if (_CRTPacked6.z > 0.001 && !outside)
    {
        float band = frac(warped.y * 0.65 + time * 0.07);
        float bar = smoothstep(0.18, 0.0, abs(band - 0.5));
        col.rgb += bar * _CRTPacked6.z * intensity * 0.22;
    }

    col.rgb = lerp(_CRTBezelColor.rgb, max(col.rgb, 0.0), bezel);
    col.a = 1.0;
    return col;
}

#endif
