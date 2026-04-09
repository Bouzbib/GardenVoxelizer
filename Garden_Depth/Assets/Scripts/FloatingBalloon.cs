using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FloatingBalloon : MonoBehaviour
{
    public float amplitude = 0.5f;   // Altura máxima del movimiento
    public float frequency = 1f;     // Velocidad del movimiento

    private Vector3 startPos;

    void Start()
    {
        startPos = transform.localPosition;
    }

    void Update()
    {
        // Movimiento senoidal
        Vector3 tempPos = startPos;
        tempPos.y += Mathf.Sin(Time.time * frequency) * amplitude;
        transform.localPosition = tempPos;
    }
}
