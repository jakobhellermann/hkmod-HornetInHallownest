using UnityEngine;
using UnityEngine.SceneManagement;

namespace Silksong;

public class NoiseMaker : MonoBehaviour
{
	public enum Intensities
	{
		Normal,
		Intense
	}

	public delegate bool NoiseEventCheck(Vector2 worldPosition);

	public delegate void NoiseEvent(Vector2 noiseSourcePos, NoiseEventCheck isNoiseInRange, Intensities intensity, bool allowOffScreen);

	[SerializeField]
	private Vector2 originOffset;

	[SerializeField]
	private float radius;

	[SerializeField]
	private Intensities intensity;

	[SerializeField]
	private bool allowOffScreen;

	[SerializeField]
	private bool createNoiseOnEnable;

	private bool hasStarted;

	public static event NoiseEvent NoiseCreated;

	public static event NoiseEvent NoiseCreatedInScene;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void RegisterReset()
	{
		SceneManager.sceneLoaded += delegate
		{
			NoiseMaker.NoiseCreatedInScene = null;
		};
	}

	private void OnDrawGizmosSelected()
	{
		Gizmos.color = Color.cyan;
		Gizmos.DrawWireSphere(base.transform.TransformPoint(originOffset), radius);
	}

	private void OnEnable()
	{
		if (hasStarted && createNoiseOnEnable)
		{
			CreateNoise();
		}
	}

	private void Start()
	{
		hasStarted = true;
		OnEnable();
	}

	public void CreateNoise()
	{
		CreateNoise(base.transform.TransformPoint(originOffset), radius, intensity, allowOffScreen);
	}

	public static void CreateNoise(Vector2 worldPosition, float radius, Intensities intensity, bool allowOffScreen = false)
	{
		NoiseMaker.NoiseCreated?.Invoke(worldPosition, (Vector2 objectPos) => Vector2.Distance(objectPos, worldPosition) <= radius, intensity, allowOffScreen);
		NoiseMaker.NoiseCreatedInScene?.Invoke(worldPosition, (Vector2 objectPos) => Vector2.Distance(objectPos, worldPosition) <= radius, intensity, allowOffScreen);
	}

	public static void CreateNoise(Vector2 worldPosition, Vector2 size, Intensities intensity, bool allowOffScreen = false)
	{
		Vector2 vector = size * 0.5f;
		Vector2 min = worldPosition - vector;
		Vector2 max = worldPosition + vector;
		NoiseMaker.NoiseCreated?.Invoke(worldPosition, (Vector2 objectPos) => objectPos.x >= min.x && objectPos.x <= max.x && objectPos.y >= min.y && objectPos.y <= max.y, intensity, allowOffScreen);
		NoiseMaker.NoiseCreatedInScene?.Invoke(worldPosition, (Vector2 objectPos) => objectPos.x >= min.x && objectPos.x <= max.x && objectPos.y >= min.y && objectPos.y <= max.y, intensity, allowOffScreen);
	}
}
