using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates a 2D shadow from the SpriteRenderer on this GameObject.
/// - Follows sprite animation, flipX, sorting layer and the object's movement.
/// - Updates live in Edit Mode, so you can tune it in the Inspector without pressing Play.
/// - The shadow object is created at runtime (hidden in the Hierarchy, never saved to the scene).
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(SpriteRenderer))]
public class SpriteShadow : MonoBehaviour
{
    public enum ShadowStyle
    {
        Ground,   // Silhouette laid on the ground from the sprite's feet, skewed by angle
        Drop      // Offset copy behind the sprite (classic drop shadow)
    }

    [Header("Shadow")]
    public bool showShadow = true;
    public ShadowStyle style = ShadowStyle.Ground;

    [Tooltip("Shadow color (alpha is ignored, use Opacity)")]
    public Color shadowColor = Color.black;

    [Tooltip("Depth / darkness: 0 = keeps the sprite's own colors, 1 = solid shadow color")]
    [Range(0f, 1f)] public float depth = 1f;

    [Tooltip("Shadow transparency: 0 = invisible, 1 = fully opaque")]
    [Range(0f, 1f)] public float opacity = 0.5f;

    [Header("Ground Style")]
    [Tooltip("Lean of the shadow in degrees. 0 = straight, positive = leans right, negative = leans left")]
    [Range(-80f, 80f)] public float angle = 45f;

    [Tooltip("Shadow length relative to the sprite height. Positive = cast upward (behind), negative = cast downward (in front)")]
    [Range(-2f, 2f)] public float length = 0.4f;

    [Tooltip("Mirror the lean when the sprite is flipped (flipX), so the light seems to follow the character")]
    public bool mirrorAngleWithFlip = false;

    [Header("Position / Sorting")]
    [Tooltip("Extra offset of the whole shadow (local units). For Drop style this is the shadow distance")]
    public Vector2 offset = new Vector2(0f, 0f);

    [Tooltip("Shadow sorting order = sprite's order + this value (negative = drawn behind the sprite)")]
    public int sortingOrderOffset = -1;

    [Tooltip("Optional. If empty, a default sprite material is created automatically")]
    public Material shadowMaterial;

    // ----- runtime -----
    SpriteRenderer source;
    GameObject shadowObj;
    MeshFilter meshFilter;
    MeshRenderer meshRenderer;
    Mesh mesh;
    Material autoMaterial;
    MaterialPropertyBlock mpb;

    readonly List<Vector3> verts = new List<Vector3>();
    readonly List<Vector2> uvs = new List<Vector2>();
    readonly List<Color> colors = new List<Color>();
    readonly List<int> tris = new List<int>();

    static readonly int MainTexId = Shader.PropertyToID("_MainTex");

    void OnEnable()
    {
        source = GetComponent<SpriteRenderer>();
        CreateShadowObject();
        UpdateShadow();
    }

    void OnDisable()
    {
        Cleanup();
    }

    void LateUpdate()
    {
        UpdateShadow();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        // Refresh right away when a value is changed in the Inspector
        if (isActiveAndEnabled && shadowObj != null) UpdateShadow();
    }
#endif

    void CreateShadowObject()
    {
        if (shadowObj != null) return;

        shadowObj = new GameObject(name + "_Shadow");
        shadowObj.hideFlags = HideFlags.HideAndDontSave;   // not shown in Hierarchy, never saved
        shadowObj.transform.SetParent(transform, false);

        meshFilter = shadowObj.AddComponent<MeshFilter>();
        meshRenderer = shadowObj.AddComponent<MeshRenderer>();
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        mesh = new Mesh { name = "SpriteShadowMesh", hideFlags = HideFlags.HideAndDontSave };
        mesh.MarkDynamic();
        meshFilter.sharedMesh = mesh;

        mpb = new MaterialPropertyBlock();
    }

    Material GetMaterial()
    {
        if (shadowMaterial != null) return shadowMaterial;
        if (autoMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null)
            {
                Debug.LogWarning("[SpriteShadow] No sprite shader found. Assign a Shadow Material in the Inspector.");
                return null;
            }
            autoMaterial = new Material(shader) { name = "SpriteShadowMaterial", hideFlags = HideFlags.HideAndDontSave };
        }
        return autoMaterial;
    }

    void UpdateShadow()
    {
        if (source == null) source = GetComponent<SpriteRenderer>();
        if (shadowObj == null) CreateShadowObject();

        Sprite sprite = source != null ? source.sprite : null;
        bool visible = showShadow && sprite != null && source.enabled && opacity > 0f;
        meshRenderer.enabled = visible;
        if (!visible) return;

        Material mat = GetMaterial();
        if (mat == null) { meshRenderer.enabled = false; return; }
        meshRenderer.sharedMaterial = mat;

        // Sorting follows the sprite
        meshRenderer.sortingLayerID = source.sortingLayerID;
        meshRenderer.sortingOrder = source.sortingOrder + sortingOrderOffset;

        // Texture of the current animation frame
        meshRenderer.GetPropertyBlock(mpb);
        mpb.SetTexture(MainTexId, sprite.texture);
        meshRenderer.SetPropertyBlock(mpb);

        BuildMesh(sprite);
    }

    void BuildMesh(Sprite sprite)
    {
        Vector2[] sv = sprite.vertices;
        Vector2[] su = sprite.uv;
        ushort[] st = sprite.triangles;

        bool flipX = source.flipX;
        bool flipY = source.flipY;

        // Bottom of the visible sprite = where the "feet" touch the ground
        float baseY = float.MaxValue;
        for (int i = 0; i < sv.Length; i++)
        {
            float y = flipY ? -sv[i].y : sv[i].y;
            if (y < baseY) baseY = y;
        }

        float a = angle;
        if (mirrorAngleWithFlip && flipX) a = -a;
        float skew = Mathf.Tan(a * Mathf.Deg2Rad);

        // Color: lerp from white (sprite's own colors) to the shadow color, then apply opacity
        Color tint = Color.Lerp(Color.white, new Color(shadowColor.r, shadowColor.g, shadowColor.b, 1f), depth);
        tint.a = opacity;

        verts.Clear(); uvs.Clear(); colors.Clear(); tris.Clear();

        for (int i = 0; i < sv.Length; i++)
        {
            float x = flipX ? -sv[i].x : sv[i].x;
            float y = flipY ? -sv[i].y : sv[i].y;

            if (style == ShadowStyle.Ground)
            {
                float h = (y - baseY) * length;   // height above the feet, squashed
                x = x + h * skew;
                y = baseY + h;
            }

            verts.Add(new Vector3(x + offset.x, y + offset.y, 0f));
            uvs.Add(su[i]);
            colors.Add(tint);
        }

        // Sprite shaders don't cull back faces, so a reversed winding (negative length / flip) still renders
        for (int i = 0; i < st.Length; i++) tris.Add(st[i]);

        mesh.Clear();
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
    }

    void Cleanup()
    {
        DestroySafe(shadowObj);
        DestroySafe(mesh);
        DestroySafe(autoMaterial);
        shadowObj = null;
        mesh = null;
        autoMaterial = null;
    }

    static void DestroySafe(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }
}