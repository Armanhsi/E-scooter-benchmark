using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class SigmaMotionController : MonoBehaviour
{
    [Header("Connection Settings")]
    [SerializeField] private string sigmaIP = "127.0.0.1";
    [SerializeField] private int sigmaPort = 10001;

    [Header("Motion Settings")]
    [SerializeField] private float accelerationMultiplier = 1.0f;
    [SerializeField] private bool enableMotion = true;
    [SerializeField] private float accelerationThreshold = 0.05f;

    [Header("Intensity Fine-Tune (Unity -> Sigma)")]
    [Tooltip("Master scale for everything sent to Sigma. Try 0.3-0.6 if the platform feels too strong.")]
    [Range(0f, 1.5f)]
    [SerializeField] private float masterIntensity = 0.25f;
    [Range(0f, 2f)]
    [SerializeField] private float surgeIntensity = 0.8f;
    [Range(0f, 2f)]
    [SerializeField] private float swayIntensity = 0.8f;
    [Range(0f, 2f)]
    [SerializeField] private float heaveIntensity = 0.55f;
    [Range(0f, 2f)]
    [SerializeField] private float pitchIntensity = 0.45f;
    [Range(0f, 2f)]
    [SerializeField] private float rollIntensity = 0.45f;
    [Range(0f, 2f)]
    [SerializeField] private float rumbleScale = 0.3f;
    [Tooltip("How much rumble also nudges pitch (0 = heave-only rumble)")]
    [Range(0f, 1f)]
    [SerializeField] private float rumblePitchBleed = 0.08f;

    [Header("Surface Vibration (formula from real rides)")]
    [Tooltip("vibration_formula_unity.json from vibration_speed_model.ipynb. Empty = built-in values.")]
    [SerializeField] private TextAsset formulaJson;
    [Tooltip("Extra scale for the surface vibration only (on top of rumbleScale and masterIntensity).")]
    [Range(0f, 3f)]
    [SerializeField] private float vibrationGain = 1f;
    [Tooltip("Scale for the sharp hits only (1 = real size).")]
    [Range(0f, 2f)]
    [SerializeField] private float hitGain = 1f;
    [Tooltip("Vibration above this frequency is not made. Set to what the platform can still follow.")]
    [Range(5f, 90f)]
    [SerializeField] private float maxVibrationFrequencyHz = 80f;
    [Tooltip("Number of sines in the base noise (more = smoother spectrum, more CPU).")]
    [SerializeField] private int vibrationComponents = 300;
    [Tooltip("Fade in/out time when entering/leaving a zone (s).")]
    [SerializeField] private float surfaceFadeTime = 0.15f;
    [Tooltip("Safety limit on the raw vibration before scaling (m/s^2).")]
    [SerializeField] private float vibrationClamp = 25f;

    [Header("Send Timing")]
    [Tooltip("On: own thread at exactly sendRateHz. Off: one packet per FixedUpdate (50 Hz unless Fixed Timestep is changed).")]
    [SerializeField] private bool sendFromThread = true;
    [SerializeField] private float sendRateHz = 200f;

    [Header("Slope Detection")]
    [SerializeField] private ScooterSlopeDetector slopeDetector;
    [SerializeField] private EscooterController scooterController;
    [Tooltip("How much turn lean (Scooter_Roll) is added into Sigma roll")]
    [Range(0f, 2f)]
    [SerializeField] private float turnLeanToSigma = 1.4f;

    private bool isRumbling = false;
    private float rumbleIntensity = 0f;
    private float rumbleFrequency = 20f;
    private double rumbleTime = 0;

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = false;
    [SerializeField] private bool showOnScreenStats = true;
    [Tooltip("Also send a copy to this port so Unity can prove packets are leaving (listen with SigmaUdpEcho)")]
    [SerializeField] private bool mirrorToLocalEcho = true;
    [SerializeField] private int echoPort = 10002;

    private UdpClient udpClient;
    private IPEndPoint endPoint;
    private IPEndPoint echoEndPoint;

    private Vector3 lastVelocity;
    private Vector3 currentAcceleration;
    private Rigidbody rb;

    private SurfaceVibrationGenerator vibration;
    private readonly List<RumbleZone> activeZones = new List<RumbleZone>();

    private struct MotionState
    {
        public float speed, surge, sway, heave, pitchDeg, rollDeg;
        public string surface;
        public float roughness;
        public bool rumbling;
        public float rumbleIntensity, rumbleFrequency;
    }
    private readonly object stateLock = new object();
    private MotionState state;
    private Thread sendThread;
    private volatile bool threadRunning;

    [Header("Live Send Stats (read-only)")]
    [SerializeField] private long packetsSent;
    [SerializeField] private float sendHz;
    [SerializeField] private float lastSpeed;
    [SerializeField] private float lastSurge;
    [SerializeField] private float lastSway;
    [SerializeField] private float lastHeave;
    [SerializeField] private float lastVibration;
    [SerializeField] private float lastPitchRad;
    [SerializeField] private float lastRollRad;
    [SerializeField] private string lastSurface = "";
    [SerializeField] private bool lastRumble;
    [SerializeField] private string lastSendError = "";

    private float hzWindowStart;
    private int hzWindowCount;
    private long hzPackets;

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        if (slopeDetector == null)
            slopeDetector = GetComponent<ScooterSlopeDetector>();
        if (scooterController == null)
            scooterController = GetComponent<EscooterController>();

        VibrationFormula formula;
        try
        {
            formula = formulaJson != null ? VibrationFormula.FromJson(formulaJson.text) : VibrationFormula.Default();
            if (formulaJson == null)
                Debug.LogWarning("SigmaMotionController: no Formula Json assigned, using built-in vibration values.");
        }
        catch (Exception e)
        {
            Debug.LogError($"SigmaMotionController: could not read the formula json ({e.Message}), using built-in values.");
            formula = VibrationFormula.Default();
        }
        float rate = sendFromThread ? sendRateHz : 1f / Time.fixedDeltaTime;
        vibration = new SurfaceVibrationGenerator(formula, rate, maxVibrationFrequencyHz, vibrationComponents,
                                                  surfaceFadeTime, Environment.TickCount);

        try
        {
            udpClient = new UdpClient();
            endPoint = new IPEndPoint(IPAddress.Parse(sigmaIP), sigmaPort);
            echoEndPoint = new IPEndPoint(IPAddress.Loopback, echoPort);
            packetsSent = 0;
            hzWindowStart = Time.unscaledTime;
            Debug.Log($"Sigma Motion Controller SEND-ONLY -> {sigmaIP}:{sigmaPort}" +
                      (slopeDetector != null ? " (slope linked)" : " (NO slope detector!)") +
                      (mirrorToLocalEcho ? $" | mirror echo :{echoPort}" : "") +
                      (sendFromThread ? $" | send thread {sendRateHz:F0} Hz" : " | send in FixedUpdate"));
        }
        catch (Exception e)
        {
            lastSendError = e.Message;
            Debug.LogError($"Failed to initialize UDP: {e.Message}");
        }

        if (sendFromThread && udpClient != null && enableMotion)
        {
            threadRunning = true;
            sendThread = new Thread(SendLoop) { IsBackground = true, Name = "SigmaSend", Priority = System.Threading.ThreadPriority.AboveNormal };
            sendThread.Start();
        }
    }

    void FixedUpdate()
    {
        if (!enableMotion) return;

        CalculateAcceleration();
        UpdateState();

        if (!sendFromThread)
        {
            MotionState s;
            lock (stateLock) s = state;
            SendMotionPacket(s, Time.fixedDeltaTime);
        }
    }

    void Update()
    {
        float now = Time.unscaledTime;
        if (now - hzWindowStart >= 1f)
        {
            long total = Interlocked.Read(ref packetsSent);
            sendHz = (total - hzPackets) / (now - hzWindowStart);
            hzPackets = total;
            hzWindowStart = now;
        }
    }

    void CalculateAcceleration()
    {
        Vector3 currentVelocity = rb != null ? rb.linearVelocity : Vector3.zero;

        currentAcceleration = (currentVelocity - lastVelocity) / Time.fixedDeltaTime;
        currentAcceleration *= accelerationMultiplier;

        if (Mathf.Abs(currentAcceleration.x) < accelerationThreshold) currentAcceleration.x = 0f;
        if (Mathf.Abs(currentAcceleration.y) < accelerationThreshold) currentAcceleration.y = 0f;
        if (Mathf.Abs(currentAcceleration.z) < accelerationThreshold) currentAcceleration.z = 0f;

        lastVelocity = currentVelocity;

        if (showDebugInfo && currentAcceleration.magnitude > 0.1f)
            Debug.Log($"Acceleration - Forward: {currentAcceleration.z:F2} Right: {currentAcceleration.x:F2} Up: {currentAcceleration.y:F2}");
    }

    void UpdateState()
    {
        float m = masterIntensity;
        float speed = rb != null ? new Vector2(rb.linearVelocity.x, rb.linearVelocity.z).magnitude : 0f;
        float pitchDeg = slopeDetector != null ? slopeDetector.slopePitch : 0f;
        float rollDeg = slopeDetector != null ? slopeDetector.slopeRoll : 0f;
        if (scooterController != null)
            rollDeg += scooterController.Scooter_Roll * turnLeanToSigma;

        RumbleZone zone = activeZones.Count > 0 ? activeZones[activeZones.Count - 1] : null;

        lock (stateLock)
        {
            state.speed = speed;
            state.surge = currentAcceleration.z * surgeIntensity * m;
            state.sway = currentAcceleration.x * swayIntensity * m;
            state.heave = currentAcceleration.y * heaveIntensity * m;
            state.pitchDeg = pitchDeg;
            state.rollDeg = rollDeg;
            state.surface = zone != null ? zone.SurfaceName : null;
            state.roughness = zone != null ? zone.roughness : 1f;
            state.rumbling = isRumbling;
            state.rumbleIntensity = rumbleIntensity;
            state.rumbleFrequency = rumbleFrequency;
        }
    }

    void SendLoop()
    {
        var watch = Stopwatch.StartNew();
        double period = 1.0 / Mathf.Max(sendRateHz, 1f);
        double next = 0;
        while (threadRunning)
        {
            double now = watch.Elapsed.TotalSeconds;
            if (now < next)
            {
                double wait = next - now;
                if (wait > 0.002) Thread.Sleep(1);
                else Thread.SpinWait(50);
                continue;
            }
            if (now - next > 0.1) next = now;
            next += period;

            MotionState s;
            lock (stateLock) s = state;
            SendMotionPacket(s, (float)period);
        }
    }

    void SendMotionPacket(MotionState s, float dt, bool includeVibration = true)
    {
        if (udpClient == null) return;

        try
        {
            float m = masterIntensity;

            float vib = 0f;
            if (includeVibration)
            {
                vibration.HitGain = hitGain;
                vibration.SetSurface(s.surface, s.roughness);
                vib = Mathf.Clamp(vibration.Next(dt, s.speed) * vibrationGain, -vibrationClamp, vibrationClamp);
            }
            float legacy = 0f;
            if (includeVibration && s.rumbling)
            {
                rumbleTime += dt;
                legacy = (float)Math.Sin(rumbleTime * s.rumbleFrequency * 2.0 * Math.PI) * s.rumbleIntensity;
            }
            float rumbleRaw = vib + legacy;

            byte[] packet = new byte[36];
            int offset = 0;
            WriteUInt32(packet, ref offset, 0);
            WriteFloat(packet, ref offset, s.speed);
            WriteFloat(packet, ref offset, s.surge);
            WriteFloat(packet, ref offset, s.sway);
            float heave = s.heave + rumbleRaw * rumbleScale * m;
            WriteFloat(packet, ref offset, heave);

            float pitchDeg = s.pitchDeg + rumbleRaw * rumblePitchBleed;
            float pitchRad = pitchDeg * Mathf.Deg2Rad * pitchIntensity * m;
            float rollRad = s.rollDeg * Mathf.Deg2Rad * rollIntensity * m;
            WriteFloat(packet, ref offset, pitchRad);
            WriteFloat(packet, ref offset, rollRad);
            WriteUInt32(packet, ref offset, 2);
            WriteFloat(packet, ref offset, s.speed * 100f);

            int bytes = udpClient.Send(packet, packet.Length, endPoint);
            if (mirrorToLocalEcho && echoEndPoint != null)
                udpClient.Send(packet, packet.Length, echoEndPoint);

            Interlocked.Increment(ref packetsSent);
            lastSpeed = s.speed;
            lastSurge = s.surge;
            lastSway = s.sway;
            lastHeave = heave;
            lastVibration = rumbleRaw;
            lastPitchRad = pitchRad;
            lastRollRad = rollRad;
            lastSurface = vibration.CurrentSurface;
            lastRumble = s.rumbling;
            lastSendError = bytes > 0 ? "" : "Send returned 0 bytes";
        }
        catch (Exception e)
        {
            lastSendError = e.Message;
            Debug.LogError($"Error sending packet: {e.Message}");
        }
    }

    void OnGUI()
    {
        if (!showOnScreenStats) return;

        const int w = 440;
        const int h = 170;
        GUI.Box(new Rect(10, 10, w, h), "Sigma UDP SEND (Unity -> Sigma)");
        GUI.Label(new Rect(20, 35, w - 20, 130),
            $"Target: {sigmaIP}:{sigmaPort}   {(sendFromThread ? "thread" : "FixedUpdate")}\n" +
            $"Packets sent: {Interlocked.Read(ref packetsSent)}   Rate: {sendHz:F0} Hz\n" +
            $"Speed: {lastSpeed:F2} m/s   Surface: {(string.IsNullOrEmpty(lastSurface) ? "-" : lastSurface)}   Old rumble: {lastRumble}\n" +
            $"Vibration: {lastVibration:F2} m/s^2\n" +
            $"Surge: {lastSurge:F2}  Sway: {lastSway:F2}  Heave: {lastHeave:F2}\n" +
            $"Pitch: {lastPitchRad:F3} rad  Roll: {lastRollRad:F3} rad\n" +
            (string.IsNullOrEmpty(lastSendError) ? "Send OK" : $"ERROR: {lastSendError}"));
    }

    void WriteUInt32(byte[] buffer, ref int offset, uint value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
        Array.Copy(bytes, 0, buffer, offset, 4);
        offset += 4;
    }

    void WriteFloat(byte[] buffer, ref int offset, float value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
        Array.Copy(bytes, 0, buffer, offset, 4);
        offset += 4;
    }

    void OnDestroy()
    {
        threadRunning = false;
        if (sendThread != null && sendThread.IsAlive)
            sendThread.Join(200);
        sendThread = null;

        if (enableMotion && udpClient != null)
        {
            activeZones.Clear();
            isRumbling = false;
            SendMotionPacket(new MotionState(), 0f, false);
        }

        if (udpClient != null)
        {
            udpClient.Close();
            udpClient = null;
            Debug.Log("Sigma Motion Controller closed");
        }
    }

    public void SetAcceleration(Vector3 acceleration)
    {
        currentAcceleration = acceleration * accelerationMultiplier;
    }

    public void EnterSurface(RumbleZone zone)
    {
        activeZones.Remove(zone);
        activeZones.Add(zone);
    }

    public void ExitSurface(RumbleZone zone)
    {
        activeZones.Remove(zone);
    }

    public void StartRumble(float intensity, float frequency)
    {
        isRumbling = true;
        rumbleIntensity = intensity;
        rumbleFrequency = frequency;
        rumbleTime = 0;
    }

    public void StopRumble()
    {
        isRumbling = false;
        rumbleIntensity = 0f;
    }
}
