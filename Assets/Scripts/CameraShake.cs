using System.Collections;
using UnityEngine;

//yos


public class CameraShake : MonoBehaviour
{
    [Header("Shake Settings")]
    public float shakeDuration = 0.1f;
    public float shakeIntensity = 0.05f;
    public AnimationCurve shakeCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);

    private Vector3 originalPosition;
    private bool isShaking = false;

    void Start()
    {
        originalPosition = transform.localPosition;
    }

    public void ShakeCamera()
    {
        if (!isShaking)
        {
            StartCoroutine(DoShake());
        }
    }

    public void ShakeCamera(float duration, float intensity)
    {
        if (!isShaking)
        {
            StartCoroutine(DoShake(duration, intensity));
        }
    }

    private IEnumerator DoShake()
    {
        yield return DoShake(shakeDuration, shakeIntensity);
    }

    private IEnumerator DoShake(float duration, float intensity)
    {
        isShaking = true;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float normalizedTime = elapsed / duration;
            float strength = shakeCurve.Evaluate(normalizedTime) * intensity;

            Vector3 randomOffset = new Vector3(
                Random.Range(-1f, 1f) * strength,
                Random.Range(-1f, 1f) * strength,
                0f
            );

            transform.localPosition = originalPosition + randomOffset;

            elapsed += Time.deltaTime;
            yield return null;
        }

        transform.localPosition = originalPosition;
        isShaking = false;
    }

    void OnDisable()
    {
        transform.localPosition = originalPosition;
        isShaking = false;
    }
}