// BurnableSurface.cs
// NEW FILE.
//
// Put this on any object that should scorch. Walls, props, and floors.
//
// How it works
//   Every object samples the heat around itself from FireSpread. Heat builds up a
//   char value from 0 for untouched to 1 for fully blackened. Char only ever goes
//   up, so scorching is permanent. Once a room burns it stays burnt.
//
// Why it samples across the bounds rather than a single point
//   A wall in your BIM import is its own object, but the floor is one large plane.
//   If we sampled only the object centre, a fire in one corner would either char
//   the entire floor at once or char nothing at all. Instead we sample a grid of
//   points across the renderer bounds and char in proportion to how much of the
//   object is actually near fire, so a big object darkens gradually and partially.
//
// Why walls need neighbour heat
//   FireSpread marks any cell containing a wall as FireState.WALL, and wall cells
//   are never ignitable. A wall cell therefore never burns on its own. The heat
//   read in FireSpread.GetHeatLevel checks the surrounding cells too, which is what
//   lets a wall char from the fire burning beside it.
//
// Rendering note
//   Darkening is done with a MaterialPropertyBlock, not by editing the material.
//   Editing the material would tint every object sharing it, and would also leak a
//   material instance per object. A property block changes only this renderer.
using UnityEngine;

public class BurnableSurface : MonoBehaviour
{
    [Header("Char build up")]
    [Tooltip("How fast char accumulates. At 0.25 an object sitting in full fire is fully black in about 4 seconds.")]
    public float charRate = 0.25f;

    [Tooltip("How dark a fully charred surface becomes. 0 is pure black, 0.1 keeps a little detail visible.")]
    [Range(0f, 0.3f)] public float finalDarkness = 0.05f;

    [Tooltip("Heat below this value is ignored, so distant fire does not slowly stain the whole building.")]
    [Range(0f, 1f)] public float heatThreshold = 0.15f;

    [Header("Bounds sampling")]
    [Tooltip("Samples taken along each axis of the object bounds. 2 gives 4 samples, 3 gives 9. Keep low for performance.")]
    [Range(1, 4)] public int samplesPerAxis = 2;

    [Tooltip("How often heat is sampled, in seconds. This does not need to run every frame.")]
    public float sampleInterval = 0.4f;

    [Header("Zone reporting, read by BurnDamageTracker")]
    [Tooltip("Which room this surface belongs to. Left empty it is resolved automatically from the Zone colliders.")]
    public string zoneName = "";

    // --- Read only, exposed for the damage tracker and the logger ---
    public float charLevel { get; private set; } = 0f;
    public bool isFullyCharred => charLevel >= 0.99f;

    // --- Private ---
    private Renderer rend;
    private MaterialPropertyBlock block;
    private FireSpread fireSpread;
    private Color originalColor = Color.white;
    private float timer = 0f;
    private bool resolved = false;

    // URP Lit uses _BaseColor. The old Standard shader used _Color. We look up the
    // ID once and fall back if the shader does not have a base colour.
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int LegacyColorId = Shader.PropertyToID("_Color");
    private int colorId;

    void Start()
    {
        rend = GetComponent<Renderer>();
        if (rend == null)
        {
            enabled = false;
            return;
        }

        fireSpread = FindAnyObjectByType<FireSpread>();
        block = new MaterialPropertyBlock();

        // Pick whichever colour property this material actually has.
        colorId = rend.sharedMaterial != null && rend.sharedMaterial.HasProperty(BaseColorId)
            ? BaseColorId
            : LegacyColorId;

        if (rend.sharedMaterial != null && rend.sharedMaterial.HasProperty(colorId))
            originalColor = rend.sharedMaterial.GetColor(colorId);

        if (string.IsNullOrEmpty(zoneName))
            zoneName = ResolveZone();

        // Stagger the first sample so hundreds of surfaces do not all tick together.
        timer = Random.Range(0f, sampleInterval);
    }

    /// <summary>
    /// Finds which room this surface sits in by testing the Zone colliders.
    /// Falls back to Unknown so nothing breaks if the object is outside every zone.
    /// </summary>
    string ResolveZone()
    {
        GameObject[] zones = GameObject.FindGameObjectsWithTag("Zone");
        Vector3 centre = GetComponent<Renderer>().bounds.center;

        foreach (GameObject zone in zones)
        {
            Collider col = zone.GetComponent<Collider>();
            if (col == null) continue;

            // Closest point equals the centre only when the centre is inside the collider.
            if ((col.ClosestPoint(centre) - centre).sqrMagnitude < 0.0001f)
                return zone.name;
        }

        return "Unknown";
    }

    void Update()
    {
        // Once fully charred there is nothing left to do. Char is permanent, so we
        // switch the component off and stop paying for it every frame.
        if (fireSpread == null || isFullyCharred)
        {
            if (isFullyCharred && !resolved)
            {
                resolved = true;
                ApplyChar();
                enabled = false;
            }
            return;
        }

        timer += Time.deltaTime;
        if (timer < sampleInterval) return;

        float dt = timer;
        timer = 0f;

        float heat = SampleHeatAcrossBounds();
        if (heat < heatThreshold) return;

        charLevel = Mathf.Clamp01(charLevel + heat * charRate * dt);
        ApplyChar();
    }

    /// <summary>
    /// Averages the heat over a grid of points across the object bounds, so a large
    /// object only chars in proportion to how much of it is actually exposed.
    /// </summary>
    float SampleHeatAcrossBounds()
    {
        Bounds b = rend.bounds;

        if (samplesPerAxis <= 1)
            return fireSpread.GetHeatLevel(b.center);

        float total = 0f;
        int count = 0;

        for (int ix = 0; ix < samplesPerAxis; ix++)
        {
            for (int iz = 0; iz < samplesPerAxis; iz++)
            {
                float fx = samplesPerAxis == 1 ? 0.5f : (float)ix / (samplesPerAxis - 1);
                float fz = samplesPerAxis == 1 ? 0.5f : (float)iz / (samplesPerAxis - 1);

                Vector3 p = new Vector3(
                    Mathf.Lerp(b.min.x, b.max.x, fx),
                    b.center.y,
                    Mathf.Lerp(b.min.z, b.max.z, fz)
                );

                total += fireSpread.GetHeatLevel(p);
                count++;
            }
        }

        return count > 0 ? total / count : 0f;
    }

    void ApplyChar()
    {
        Color charred = originalColor * Mathf.Lerp(1f, finalDarkness, charLevel);
        charred.a = originalColor.a;

        rend.GetPropertyBlock(block);
        block.SetColor(colorId, charred);
        rend.SetPropertyBlock(block);
    }
}