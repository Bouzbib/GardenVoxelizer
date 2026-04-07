using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class SquareVideoClock : MonoBehaviour
{
    public bool otherMethod;
    public int packCount = 8;
    public int currentPack;          // set externally by your voxelizer
    public float amplitude = 0.9f;

    public bool sinusIsMaster = true;

    [Header("Visual master sinus")]
    [Range(0f, 1f)] public float visualPhaseOffset = 0f;   // independent visual-only phase
    public int VisualMasterPack;

    [Range(0, 1)]
    public float offsetPhase;

    public enum Waveform
    {
        Sine,
        Square
    }
    public Waveform waveform = Waveform.Sine;

    AudioSource audioSource;

    // Audio state
    double sampleRate;
    double phase;                    // continuous phase (radians)
    float offset01, phase01;
    double phasePerSample;           // actual current increment
    double targetPhasePerSample;     // desired increment after correction

    int lastPack = -1;

    public int globalSlice;
    int countWithOffset;
    float dspPhase;

    [Header("Sync smoothing")]
    [Range(0f, 1f)] public float phaseLockStrength = 0.15f;   // kept for compatibility; used as phase nudge strength
    [Range(0f, 1f)] public float freqSmooth = 0.02f;          // smoothing of frequency correction
    public float maxFreqCorrectionHz = 1.5f;                  // clamp correction during hitches

    [Header("Debug")]
    public float sineValue, sineViz;
    public bool interlaced;
    public bool IsGoingUp { get; private set; }
    public bool IsGoingDown => !IsGoingUp;
    public int packDebug;

    [Header("Hybrid lock")]
    [Range(0f, 1f)] public float packEdgePhaseNudge = 0.35f;  // partial snap on pack transition
    public float hardResyncThresholdDeg = 20f;                // only hard snap if very far off
    public float projectorHz = 120f;

    // latest video event, written from render thread, consumed by audio thread
    volatile int latestPackEvent = -1;
    double latestPackEventDspTime = -1.0;

    int lastRenderedPack = -1;
    int lastConsumedPackEvent = -999;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.playOnAwake = true;
        audioSource.loop = true;
        audioSource.spatialBlend = 0f;

        audioSource.clip = AudioClip.Create(
            "VideoClockDummy",
            1024,
            1,
            AudioSettings.outputSampleRate,
            false
        );

        audioSource.loop = true;

        sampleRate = AudioSettings.outputSampleRate;

        double baseFrequency = projectorHz / packCount;   // e.g. 120 / 8 = 15 Hz
        phasePerSample = 2.0 * Mathf.PI * baseFrequency / sampleRate;
        targetPhasePerSample = phasePerSample;

        audioSource.Play();
    }

    void Update()
    {
        if (Input.GetKey(KeyCode.Keypad0))
        {
            this.offsetPhase = (float)(offsetPhase + 0.01f) % 1;
        }
        if (Input.GetKey(KeyCode.Keypad2))
        {
            this.offsetPhase = (float)(offsetPhase - 0.001f) % 1;
        }
        if (Input.GetKey(KeyCode.Keypad3))
        {
            this.offsetPhase = (float)(offsetPhase + 0.001f) % 1;
        }
        if (Input.GetKey(KeyCode.Keypad1))
        {
            visualPhaseOffset = (visualPhaseOffset + 0.01f) % 1f;
        }

        offset01 = offsetPhase * Mathf.PI * 2f;

        double baseFrequency = projectorHz / packCount;   // e.g. 120 / 8 = 15 Hz
        // float baseFrequency = 1f / (Time.unscaledDeltaTime*packCount);
        // normalized mechanical phase [0,1)
        double mechPhase01 = Mathf.Repeat((float)(AudioSettings.dspTime * baseFrequency + offsetPhase), 1f);

        int mechPack = Mathf.FloorToInt((float)(mechPhase01 * packCount));
        mechPack = Mathf.Clamp(mechPack, 0, packCount - 1);

        // normalized visual phase [0,1)
        double visPhase01 = Mathf.Repeat((float)(mechPhase01 + visualPhaseOffset), 1f);

        int visPack = Mathf.FloorToInt((float)(visPhase01 * packCount));
        visPack = Mathf.Clamp(visPack, 0, packCount - 1);

        VisualMasterPack = visPack;

        if (sinusIsMaster)
        {
            currentPack = mechPack;
            IsGoingUp = currentPack < (packCount / 2);
        }
        // if(currentPack == 0)
        // {
        //     packDebug = VisualMasterPack;
        //     Debug.Log("Viz: " + VisualMasterPack);
        // }
        
        
    }

    public void SetZ01(float z01)
    {
        phase = z01 * 2.0 * Mathf.PI;
        WrapPhase();
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        // Smooth frequency continuously on audio thread
        phasePerSample += (targetPhasePerSample - phasePerSample) * freqSmooth;

        // Consume latest pack event once per callback
        int packEvent = latestPackEvent;
        double packEventTime = latestPackEventDspTime;

        if (packEvent >= 0 && packEvent != lastConsumedPackEvent && packEventTime > 0.0)
        {
            ApplyPackEdgeLock(packEvent, packEventTime);
            lastConsumedPackEvent = packEvent;
        }

        for (int i = 0; i < data.Length; i += channels)
        {
            float sample;
            float sampleViz;
            double p = phase + offset01;
            double q = phase + offset01 + visualPhaseOffset * Mathf.PI * 2.0;

            switch (waveform)
            {
                case Waveform.Square:
                    sample = ((p % (Mathf.PI * 2.0)) < Mathf.PI) ? amplitude : -amplitude;
                    sampleViz = ((q % (Mathf.PI * 2.0)) < Mathf.PI) ? amplitude : -amplitude;
                    break;

                default:
                    sample = Mathf.Sin((float)p) * amplitude;
                    sampleViz = Mathf.Sin((float)q) * amplitude;
                    break;
            }

            sineValue = sample;
            sineViz = sampleViz;

            for (int c = 0; c < channels; c++)
                data[i + c] = sample;

            phase += phasePerSample;
            WrapPhase();
        }
    }

    // This is the VIDEO CLOCK
    void OnCameraPostRender(Camera cam)
    {
       // if (sinusIsMaster)
       //  return;

        IsGoingUp = currentPack < (packCount / 2);

        if (currentPack != lastRenderedPack)
        {
            lastRenderedPack = currentPack;
            latestPackEvent = currentPack;
            latestPackEventDspTime = AudioSettings.dspTime;
        }
    }

    void ApplyPackEdgeLock(int pack, double eventDspTime)
    {
        double baseFrequency = projectorHz / packCount;

        // phase corresponding to the START of this displayed pack
        double packPhase = (double)pack / packCount * Mathf.PI * 2.0;

        // estimate where that phase should be "now"
        double now = AudioSettings.dspTime;
        double dt = now - eventDspTime;
        if (dt < 0.0) dt = 0.0;

        double expectedPhaseNow = packPhase + dt * (2.0 * Mathf.PI * baseFrequency);
        expectedPhaseNow = WrapPhaseValue(expectedPhaseNow);

        // compare current audio phase to expected phase
        double error = DeltaAngleRad(phase, expectedPhaseNow);
        double absErrorDeg = Mathf.Abs((float)(error * Mathf.Rad2Deg));

        // 1) phase correction
        if (absErrorDeg >= hardResyncThresholdDeg)
        {
            // only if seriously out of sync
            phase = expectedPhaseNow;
        }
        else
        {
            // gentle partial correction
            double nudge = Mathf.Max(phaseLockStrength, packEdgePhaseNudge);
            phase += error * nudge;
            WrapPhase();
        }

        // 2) frequency correction
        // convert phase error into a small temporary frequency shift
        double correctionHz = Mathf.Clamp(
            (float)(error * phaseLockStrength),
            -maxFreqCorrectionHz,
            maxFreqCorrectionHz
        );

        double correctedFrequency = baseFrequency + correctionHz;
        targetPhasePerSample = 2.0 * Mathf.PI * correctedFrequency / sampleRate;

        lastPack = pack;
    }

    static double DeltaAngleRad(double current, double target)
    {
        double delta = target - current;
        while (delta > Mathf.PI) delta -= Mathf.PI * 2.0;
        while (delta < -Mathf.PI) delta += Mathf.PI * 2.0;
        return delta;
    }

    void WrapPhase()
    {
        while (phase >= Mathf.PI * 2.0) phase -= Mathf.PI * 2.0;
        while (phase < 0.0) phase += Mathf.PI * 2.0;
    }

    static double WrapPhaseValue(double value)
    {
        while (value >= Mathf.PI * 2.0) value -= Mathf.PI * 2.0;
        while (value < 0.0) value += Mathf.PI * 2.0;
        return value;
    }

    // void OnEnable()
    // {
    //     Camera.onPostRender -= OnCameraPostRender;
    //     if (!sinusIsMaster)
    //         Camera.onPostRender += OnCameraPostRender;
    // }

    // void OnDisable()
    // {
    //     Camera.onPostRender -= OnCameraPostRender;
    // }

    // void OnValidate()
    // {
    //     Camera.onPostRender -= OnCameraPostRender;
    //     if (Application.isPlaying && !sinusIsMaster)
    //         Camera.onPostRender += OnCameraPostRender;
    // }

    public void NotifyDisplayedPack(int pack)
    {
        IsGoingUp = pack < (packCount / 2);

        if (pack != lastRenderedPack)
        {
            lastRenderedPack = pack;
            latestPackEvent = pack;
            latestPackEventDspTime = AudioSettings.dspTime;
        }
    }
}