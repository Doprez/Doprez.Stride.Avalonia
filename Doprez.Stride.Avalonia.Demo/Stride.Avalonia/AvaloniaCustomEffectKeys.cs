using System.Linq;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Shaders;

namespace Stride.Avalonia;

/// <summary>
/// Resolves shader parameter keys for custom SDSL effects at runtime
/// by inspecting the compiled <see cref="EffectBytecode"/> reflection data.
/// This works for effects compiled at runtime (not just build-time).
/// </summary>
internal sealed class CustomEffectParameterKeys
{
    public ValueParameterKey<Matrix>? WorldViewProjection { get; private set; }
    public ValueParameterKey<Matrix>? World { get; private set; }
    public ObjectParameterKey<Texture>? UITexture { get; private set; }
    public ValueParameterKey<float>? Time { get; private set; }
    public bool IsResolved { get; private set; }

    /// <summary>
    /// Resolves parameter keys by inspecting the compiled effect's reflection.
    /// Called once after the effect is successfully compiled.
    /// </summary>
    public void Resolve(EffectInstance effectInstance, string effectName)
    {
        if (IsResolved) return;

        var reflection = effectInstance.Effect.Bytecode.Reflection;
        var log = Stride.Core.Diagnostics.GlobalLogger.GetLogger("CustomEffectParameterKeys");

        // Search constant buffer members for our parameter names
        foreach (var cb in reflection.ConstantBuffers)
        {
            foreach (var member in cb.Members)
            {
                var keyName = member.KeyInfo.KeyName;
                var key = member.KeyInfo.Key;

                log.Info($"  CB member: {member.KeyInfo.KeyName} -> {key?.Name ?? "(null)"}");

                if (keyName.EndsWith(".WorldViewProjection") && key is ValueParameterKey<Matrix> wvpKey)
                    WorldViewProjection = wvpKey;
                else if (keyName.EndsWith(".World") && key is ValueParameterKey<Matrix> worldKey)
                    World = worldKey;
                else if (keyName.EndsWith(".Time") && key is ValueParameterKey<float> timeKey)
                    Time = timeKey;
            }
        }

        // Search resource bindings for the texture
        foreach (var rb in reflection.ResourceBindings)
        {
            var keyName = rb.KeyInfo.KeyName;
            var key = rb.KeyInfo.Key;

            log.Info($"  Resource: {rb.KeyInfo.KeyName} -> {key?.Name ?? "(null)"}");

            if (keyName.EndsWith(".UITexture") && key is ObjectParameterKey<Texture> texKey)
                UITexture = texKey;
        }

        IsResolved = true;
        log.Info($"Resolved keys for '{effectName}': WVP={WorldViewProjection != null}, World={World != null}, UITexture={UITexture != null}, Time={Time != null}");
    }
}
