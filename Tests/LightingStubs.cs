// Narrow Unity stand-ins for the production material-assignment helper only.
// Live lighting/rendering is not simulated by this test host.
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object? a, Object? b)
        {
            bool aNull = ReferenceEquals(a, null) || a.Destroyed;
            bool bNull = ReferenceEquals(b, null) || b.Destroyed;
            return aNull || bNull ? aNull && bNull : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object? a, Object? b) => !(a == b);
        public override bool Equals(object? other) => ReferenceEquals(this, other);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }

    public sealed class Material : Object { }
    public sealed class SpriteRenderer : Object
    {
        public Material? sharedMaterial;
        public string color = "white";
        public string sprite = "custom-art";
    }
    public static class Resources
    {
        public static Material? Diffuse;
        public static int LoadCalls;
        public static string? LastPath;
        public static T? Load<T>(string path) where T : Object
        { LoadCalls++; LastPath = path; return Diffuse as T; }
    }
}

namespace SilverpineMods.CustomEnemyLoader
{
    internal static class Plugin { internal static readonly TestLog Log = new(); }
    internal sealed class TestLog
    {
        internal readonly List<string> Messages = new();
        internal void LogWarning(string message) => Messages.Add(message);
    }
}
