using System;
using UnityEngine;

[Serializable]
public class SurfaceVibrationParams
{
    public string name;
    public float K;
    public float[] shapeF;
    public float[] shapeS;
    public float wheelK;
    public float wheelAlpha;
    public float hitsPerM;
    public float[] hitSizes;
    public float[] hitShape;
    public float speedMin;
    public float speedMax;
}

[Serializable]
public class VibrationFormula
{
    public float vRef = 4f;
    public float alpha = 0.43f;
    public float wheelC = 0.69f;
    public float hitShapeFs = 200f;
    public SurfaceVibrationParams[] surfaces;

    public static VibrationFormula FromJson(string json)
    {
        var f = JsonUtility.FromJson<VibrationFormula>(json);
        if (f == null || f.surfaces == null || f.surfaces.Length == 0)
            throw new Exception("vibration formula json has no surfaces");
        return f;
    }

    public SurfaceVibrationParams Find(string surfaceName)
    {
        if (string.IsNullOrEmpty(surfaceName) || surfaces == null) return null;
        foreach (var s in surfaces)
            if (string.Equals(s.name, surfaceName, StringComparison.OrdinalIgnoreCase))
                return s;
        return null;
    }

    public static VibrationFormula Default()
    {
        int n = 160;
        var f = new float[n];
        var s = new float[n];
        float total = 0f;
        for (int i = 0; i < n; i++)
        {
            f[i] = 0.5f * (i + 1);
            float g5 = Mathf.Exp(-0.5f * Mathf.Pow((f[i] - 5f) / 1.5f, 2f));
            float g18 = Mathf.Exp(-0.5f * Mathf.Pow((f[i] - 19f) / 6f, 2f));
            s[i] = 1.2f * g5 + 0.9f * g18 + 0.6f * Mathf.Exp(-f[i] / 22f);
            total += s[i] * 0.5f;
        }
        for (int i = 0; i < n; i++) s[i] /= total;

        int m = 61;
        var hit = new float[m];
        for (int i = 0; i < m; i++)
        {
            float t = (i - 30) / 200f;
            hit[i] = Mathf.Exp(-Mathf.Pow(t / 0.006f, 2f)) - 0.3f * Mathf.Exp(-Mathf.Pow((Mathf.Abs(t) - 0.016f) / 0.006f, 2f));
        }

        return new VibrationFormula
        {
            vRef = 4f,
            alpha = 0.43f,
            wheelC = 0.69f,
            hitShapeFs = 200f,
            surfaces = new[]
            {
                new SurfaceVibrationParams { name = "sidewalk", K = 2.12f, shapeF = f, shapeS = s, wheelK = 0.21f, wheelAlpha = 0.28f,
                    hitsPerM = 0.33f, hitSizes = new[] { 10.1f, 11.5f, 13.9f, 18.1f, 26.7f }, hitShape = hit, speedMin = 2.5f, speedMax = 5.8f },
                new SurfaceVibrationParams { name = "asphalt", K = 1.62f, shapeF = f, shapeS = s, wheelK = 0.21f, wheelAlpha = 0.49f,
                    hitsPerM = 0.08f, hitSizes = new[] { 9.8f, 10.8f, 12.4f, 15.6f, 26.4f }, hitShape = hit, speedMin = 3.3f, speedMax = 7.6f },
            }
        };
    }
}

public class SurfaceVibrationGenerator
{
    readonly VibrationFormula formula;
    readonly System.Random rng;
    readonly float sampleRateHz;
    readonly float maxFrequencyHz;
    readonly int componentCount;
    readonly float fadeTime;

    SurfaceVibrationParams current;
    bool active;
    float roughness = 1f;
    float gain;

    double[] freq = new double[0];
    double[] amp = new double[0];
    double[] phase = new double[0];
    double wheelPhase;

    double hitTime = -1;
    float hitAmp;

    const double TwoPi = 2.0 * Math.PI;

    public SurfaceVibrationGenerator(VibrationFormula formula, float sampleRateHz, float maxFrequencyHz, int componentCount,
                                     float fadeTime, int seed)
    {
        this.formula = formula;
        this.sampleRateHz = sampleRateHz;
        this.maxFrequencyHz = maxFrequencyHz;
        this.componentCount = Mathf.Max(componentCount, 10);
        this.fadeTime = Mathf.Max(fadeTime, 1e-3f);
        rng = new System.Random(seed);
    }

    public float HitGain = 1f;

    public string CurrentSurface => active && current != null ? current.name : "";

    public void SetSurface(string surfaceName, float zoneRoughness)
    {
        roughness = Mathf.Max(zoneRoughness, 0f);
        var p = formula.Find(surfaceName);
        if (p == null)
        {
            active = false;
            return;
        }
        if (p != current)
            Build(p);
        active = true;
    }

    void Build(SurfaceVibrationParams p)
    {
        current = p;
        var F = p.shapeF;
        var S = p.shapeS;
        double total = 0;
        for (int i = 1; i < F.Length; i++)
            total += 0.5 * (S[i] + S[i - 1]) * (F[i] - F[i - 1]);
        if (total <= 0) total = 1;

        float fMin = Mathf.Max(0.5f, F[0]);
        float fMax = Mathf.Min(Mathf.Min(maxFrequencyHz, 0.45f * sampleRateHz), F[F.Length - 1]);
        int n = componentCount;
        double df = (fMax - fMin) / n;
        freq = new double[n];
        amp = new double[n];
        phase = new double[n];
        for (int i = 0; i < n; i++)
        {
            freq[i] = fMin + (i + rng.NextDouble()) * df;
            amp[i] = Math.Sqrt(2.0 * Interp(F, S, (float)freq[i]) * df / total);
            phase[i] = rng.NextDouble() * TwoPi;
        }
    }

    public float Next(float dt, float speed)
    {
        float target = active ? 1f : 0f;
        gain += (target - gain) * Mathf.Min(1f, dt / fadeTime);
        if (current == null || (!active && gain < 1e-4f))
        {
            gain = active ? gain : 0f;
            hitTime = -1;
            return 0f;
        }

        var p = current;
        float v = Mathf.Max(speed, 0f);
        float speedFactor = Mathf.Pow(v / formula.vRef, formula.alpha);

        double noise = 0;
        for (int i = 0; i < freq.Length; i++)
        {
            noise += amp[i] * Math.Sin(phase[i]);
            phase[i] += TwoPi * freq[i] * dt;
            if (phase[i] > TwoPi) phase[i] -= TwoPi;
        }
        float a = p.K * roughness * speedFactor * (float)noise;

        if (formula.wheelC > 0f && p.wheelK > 0f && v > 0.1f)
        {
            wheelPhase += TwoPi * v / formula.wheelC * dt;
            if (wheelPhase > TwoPi) wheelPhase -= TwoPi;
            a += p.wheelK * Mathf.Pow(v / formula.vRef, p.wheelAlpha) * (float)Math.Sin(wheelPhase);
        }

        if (hitTime < 0 && p.hitShape != null && p.hitShape.Length > 1 && rng.NextDouble() < p.hitsPerM * v * dt)
        {
            hitTime = 0;
            hitAmp = HitSize(p) * roughness * HitGain * (rng.NextDouble() < 0.5 ? -1f : 1f);
        }
        if (hitTime >= 0)
        {
            double idx = hitTime * formula.hitShapeFs;
            int i0 = (int)idx;
            if (i0 >= p.hitShape.Length - 1)
            {
                hitTime = -1;
            }
            else
            {
                float w = (float)(idx - i0);
                a += hitAmp * Mathf.Lerp(p.hitShape[i0], p.hitShape[i0 + 1], w);
                hitTime += dt;
            }
        }

        return gain * a;
    }

    float HitSize(SurfaceVibrationParams p)
    {
        var q = p.hitSizes;
        if (q == null || q.Length == 0) return 0f;
        if (q.Length != 5) return q[q.Length / 2];
        float[] at = { 0.05f, 0.25f, 0.5f, 0.75f, 0.95f };
        float u = (float)rng.NextDouble();
        if (u <= at[0]) return q[0];
        for (int i = 1; i < 5; i++)
            if (u <= at[i])
                return Mathf.Lerp(q[i - 1], q[i], (u - at[i - 1]) / (at[i] - at[i - 1]));
        return q[4];
    }

    static float Interp(float[] x, float[] y, float xi)
    {
        if (xi <= x[0]) return y[0];
        int n = x.Length;
        if (xi >= x[n - 1]) return y[n - 1];
        int lo = 0, hi = n - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (x[mid] <= xi) lo = mid; else hi = mid;
        }
        return Mathf.Lerp(y[lo], y[hi], (xi - x[lo]) / (x[hi] - x[lo]));
    }
}
