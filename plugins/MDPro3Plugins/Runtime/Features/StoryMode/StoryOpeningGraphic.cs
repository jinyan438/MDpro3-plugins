using UnityEngine;
using UnityEngine.UI;

namespace MDPro3.Plugins.Features.StoryMode
{
    // Small, texture-free UI meshes. All motion uses the opening's unscaled clock;
    // no materials, render textures, particle systems or downloaded assets are needed.
    internal sealed class StoryOpeningGraphic : MaskableGraphic
    {
        internal enum Shape { Halo, Orbit, Rays, Dust, Shine }
        internal Shape Form;
        internal float Clock, Strength = 1f;
        internal bool Rainbow;

        internal void Animate(float clock, float strength = 1f)
        {
            Clock = clock; Strength = strength;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = rectTransform.rect;
            var center = rect.center;
            float radius = Mathf.Min(rect.width, rect.height) * .5f;
            switch (Form)
            {
                case Shape.Halo:
                    for (int ring = 0; ring < 10; ring++)
                    {
                        float a = ring / 10f, b = (ring + 1) / 10f;
                        Ring(mesh, center, radius * a, radius * b, 0, 360,
                            Tint(Mathf.Pow(1f - a, 3f)), Tint(Mathf.Pow(1f - b, 3f)), 64);
                    }
                    break;
                case Shape.Orbit:
                    for (int ring = 0; ring < 3; ring++)
                    {
                        float r = radius * (1 - ring * .085f);
                        float rotation = Clock * (ring == 1 ? -13 : 9) + ring * 52;
                        for (int arc = 0; arc < 6; arc++)
                            Ring(mesh, center, r - (ring == 1 ? 2 : 1), r, rotation + arc * 60,
                                rotation + arc * 60 + (ring == 1 ? 26 : 52), Tint(.6f), Tint(.6f), 14);
                    }
                    for (int i = 0; i < 48; i++)
                    {
                        float angle = i * 7.5f + Clock * 3;
                        Ring(mesh, center, radius * .72f, radius * (i % 4 == 0 ? .75f : .735f),
                            angle, angle + .4f, Tint(.5f), Tint(.5f), 1);
                    }
                    break;
                case Shape.Rays:
                    for (int i = 0; i < 22; i++)
                    {
                        float angle = i * 137.5f + Clock * 5;
                        float length = radius * (.65f + .35f * Mathf.Sin(i * 27 + Clock));
                        var inner = center + Direction(angle) * radius * .08f;
                        var c = Tint((.13f + .1f * Mathf.Sin(i + Clock * 2)) * Strength);
                        Triangle(mesh, inner, center + Direction(angle - 1.5f) * length,
                            center + Direction(angle + 1.5f) * length, c, Color.clear, Color.clear);
                    }
                    break;
                case Shape.Dust:
                    for (int i = 0; i < 65; i++)
                    {
                        float seed = Mathf.Repeat(Mathf.Sin(i * 12.9898f + 2) * 43758.5453f, 1);
                        float travel = Mathf.Repeat(seed + Clock * (.025f + .015f * (i % 3)), 1);
                        var pos = new Vector2(rect.xMin + Mathf.Repeat(i * .381966f, 1) * rect.width,
                            rect.yMin + travel * rect.height);
                        float size = i % 7 == 0 ? 2.5f : 1.1f;
                        var c = Tint(Mathf.Sin(travel * Mathf.PI) * (.25f + .4f * seed));
                        Quad(mesh, pos + Vector2.up * size * 2, pos + Vector2.right * size,
                            pos + Vector2.down * size * 2, pos + Vector2.left * size, c, c, c, c);
                    }
                    break;
                case Shape.Shine:
                    float x = Mathf.Lerp(rect.xMin - rect.width, rect.xMax + rect.width, Mathf.Repeat(Clock, 1));
                    float width = rect.width * .28f;
                    Quad(mesh, new Vector2(x - width, rect.yMin), new Vector2(x, rect.yMin),
                        new Vector2(x + rect.width * .55f, rect.yMax), new Vector2(x + rect.width * .55f - width, rect.yMax),
                        Color.clear, Tint(.26f * Strength), Tint(.26f * Strength), Color.clear);
                    break;
            }
        }

        private Color Tint(float alpha)
        {
            var c = color; c.a *= alpha; return c;
        }
        private static Vector2 Direction(float degrees) => new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));

        private void Ring(VertexHelper mesh, Vector2 center, float inner, float outer, float start, float end,
            Color inside, Color outside, int segments)
        {
            for (int i = 0; i < segments; i++)
            {
                float a = Mathf.Lerp(start, end, (float)i / segments);
                float b = Mathf.Lerp(start, end, (float)(i + 1) / segments);
                var ca = inside; var cb = outside;
                if (Rainbow)
                {
                    float hueAngle = a * Mathf.Deg2Rad + Clock * .44f;
                    var hue = new Color(.78f + .22f * Mathf.Cos(hueAngle),
                        .78f + .22f * Mathf.Cos(hueAngle + 2.094f), .78f + .22f * Mathf.Cos(hueAngle + 4.189f));
                    ca = new Color(hue.r, hue.g, hue.b, inside.a);
                    cb = new Color(hue.r, hue.g, hue.b, outside.a);
                }
                Quad(mesh, center + Direction(a) * inner, center + Direction(a) * outer,
                    center + Direction(b) * outer, center + Direction(b) * inner, ca, cb, cb, ca);
            }
        }

        private static void Triangle(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Color ca, Color cb, Color cc)
        {
            int index = mesh.currentVertCount;
            mesh.AddVert(a, ca, Vector2.zero); mesh.AddVert(b, cb, Vector2.zero); mesh.AddVert(c, cc, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2);
        }
        private static void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color ca, Color cb, Color cc, Color cd)
        {
            int index = mesh.currentVertCount;
            mesh.AddVert(a, ca, Vector2.zero); mesh.AddVert(b, cb, Vector2.zero);
            mesh.AddVert(c, cc, Vector2.zero); mesh.AddVert(d, cd, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2); mesh.AddTriangle(index, index + 2, index + 3);
        }
    }
}
