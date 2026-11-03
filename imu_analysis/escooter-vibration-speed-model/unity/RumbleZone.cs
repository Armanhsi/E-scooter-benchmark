using UnityEngine;

public class RumbleZone : MonoBehaviour
{
    public enum Surface { Sidewalk, Asphalt }

    [Header("Surface")]
    public Surface surface = Surface.Sidewalk;

    [Tooltip("Roughness of this patch. 1 = average of the real rides; real patches of the same surface were " +
             "mostly between 0.7 and 1.3, rough spots (bridge joints, broken asphalt) up to about 2.")]
    [Range(0.5f, 2f)]
    public float roughness = 1f;

    [Header("Old fixed sine (only used when Use Formula is off)")]
    public bool useFormula = true;
    [Range(0f, 10f)]
    public float rumbleIntensity = 3f;
    [Range(1f, 50f)]
    public float rumbleFrequency = 20f;

    [Header("Debug")]
    public bool showDebug = true;

    private SigmaMotionController activeScooter = null;

    public string SurfaceName => surface.ToString().ToLowerInvariant();

    private void OnTriggerEnter(Collider other)
    {
        var scooter = FindScooter(other);
        if (scooter == null || scooter == activeScooter) return;

        activeScooter = scooter;
        if (useFormula)
            activeScooter.EnterSurface(this);
        else
            activeScooter.StartRumble(rumbleIntensity, rumbleFrequency);
        if (showDebug) Debug.Log($"Entered {SurfaceName} zone: {gameObject.name} (roughness {roughness:F2})");
    }

    private void OnTriggerExit(Collider other)
    {
        var scooter = FindScooter(other);
        if (scooter == null || scooter != activeScooter) return;

        if (useFormula)
            activeScooter.ExitSurface(this);
        else
            activeScooter.StopRumble();
        if (showDebug) Debug.Log($"Exited {SurfaceName} zone: {gameObject.name}");
        activeScooter = null;
    }

    private void OnDisable()
    {
        if (activeScooter == null) return;
        if (useFormula) activeScooter.ExitSurface(this);
        else activeScooter.StopRumble();
        activeScooter = null;
    }

    private static SigmaMotionController FindScooter(Collider other)
    {
        return other.GetComponentInParent<SigmaMotionController>();
    }
}
