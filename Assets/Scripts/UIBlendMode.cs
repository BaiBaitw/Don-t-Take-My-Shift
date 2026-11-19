using UnityEngine;
using UnityEngine.UI;
using RBlend = UnityEngine.Rendering.BlendMode;
using RBlendOp = UnityEngine.Rendering.BlendOp;

/// <summary>
/// Attach to any UI Graphic (Image, RawImage, Text...) to blend it over
/// whatever is drawn underneath, like a Photoshop layer blend mode.
/// Requires the "UI/BlendMode" shader (UIBlendMode.shader).
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Graphic))]
public class UIBlendMode : MonoBehaviour, IMaterialModifier
{
    public enum BlendType
    {
        Normal,
        Additive,   // Linear Dodge (Add)
        Multiply,
        Screen,
        Overlay,    // Approximation: 2 * src * dst, 50% gray is neutral
        Darken,
        Lighten,
        Subtract,
    }

    [SerializeField] private BlendType blendType = BlendType.Overlay;

    [Tooltip("Falls back to Shader.Find(\"UI/BlendMode\") if empty. Keep this reference so the shader is included in builds.")]
    [SerializeField] private Shader shader;

    private Graphic _graphic;
    private Material _material;

    public BlendType Type
    {
        get => blendType;
        set
        {
            if (blendType == value) return;
            blendType = value;
            SetDirty();
        }
    }

    private void Reset()
    {
        shader = Shader.Find("UI/BlendMode");
    }

    private void OnEnable() => SetDirty();
    private void OnDisable() => SetDirty();

#if UNITY_EDITOR
    private void OnValidate() => SetDirty();
#endif

    private void OnDestroy()
    {
        if (_material == null) return;
        if (Application.isPlaying) Destroy(_material);
        else DestroyImmediate(_material);
    }

    private void SetDirty()
    {
        if (_graphic == null) _graphic = GetComponent<Graphic>();
        if (_graphic != null) _graphic.SetMaterialDirty();
    }

    public Material GetModifiedMaterial(Material baseMaterial)
    {
        if (!isActiveAndEnabled) return baseMaterial;

        if (shader == null) shader = Shader.Find("UI/BlendMode");
        if (shader == null)
        {
            Debug.LogWarning("[UIBlendMode] Shader 'UI/BlendMode' not found.", this);
            return baseMaterial;
        }

        if (_material == null)
        {
            _material = new Material(shader)
            {
                name = "UIBlendMode (Instance)",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        // Keep stencil settings from Mask, etc.
        _material.CopyPropertiesFromMaterial(baseMaterial);

        RBlend src, dst;
        RBlendOp op = RBlendOp.Add;

        switch (blendType)
        {
            case BlendType.Additive: src = RBlend.One; dst = RBlend.One; break;
            case BlendType.Multiply: src = RBlend.DstColor; dst = RBlend.Zero; break;
            case BlendType.Screen: src = RBlend.OneMinusDstColor; dst = RBlend.One; break;
            case BlendType.Overlay: src = RBlend.DstColor; dst = RBlend.SrcColor; break;
            case BlendType.Darken: src = RBlend.One; dst = RBlend.One; op = RBlendOp.Min; break;
            case BlendType.Lighten: src = RBlend.One; dst = RBlend.One; op = RBlendOp.Max; break;
            case BlendType.Subtract: src = RBlend.One; dst = RBlend.One; op = RBlendOp.ReverseSubtract; break;
            default: src = RBlend.SrcAlpha; dst = RBlend.OneMinusSrcAlpha; break;
        }

        _material.SetFloat("_BlendMode", (int)blendType);
        _material.SetInt("_SrcBlend", (int)src);
        _material.SetInt("_DstBlend", (int)dst);
        _material.SetInt("_BlendOp", (int)op);

        return _material;
    }
}