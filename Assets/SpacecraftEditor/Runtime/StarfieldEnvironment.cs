using UnityEngine;

namespace SpacecraftEditor
{
    public sealed class StarfieldEnvironment : MonoBehaviour
    {
        [SerializeField] private int starCount = 360;
        [SerializeField] private int randomSeed = 7319;

        private void Awake()
        {
            if (transform.Find("ProceduralStars") != null)
                return;

            var starsObject = new GameObject("ProceduralStars");
            starsObject.transform.SetParent(transform, false);
            var particles = starsObject.AddComponent<ParticleSystem>();
            var renderer = starsObject.GetComponent<ParticleSystemRenderer>();
            var shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            renderer.material = new Material(shader);
            renderer.material.color = Color.white;

            var main = particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = starCount;
            main.startLifetime = 99999f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.12f);
            var emission = particles.emission;
            emission.enabled = false;

            Random.InitState(randomSeed);
            for (var i = 0; i < starCount; i++)
            {
                var direction = Random.onUnitSphere;
                var distance = Random.Range(28f, 52f);
                var colorChoice = Random.value;
                var color = colorChoice < 0.12f
                    ? new Color(0.35f, 0.8f, 1f, 1f)
                    : colorChoice > 0.9f
                        ? new Color(1f, 0.58f, 0.32f, 1f)
                        : Color.white;
                var emit = new ParticleSystem.EmitParams
                {
                    position = direction * distance,
                    startColor = color,
                    startSize = Random.Range(0.035f, 0.12f),
                    startLifetime = 99999f
                };
                particles.Emit(emit, 1);
            }
        }
    }
}
