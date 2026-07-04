// MultiShake.cs
// ใช้สั่นหลายวัตถุพร้อมกัน โดยควบคุมจาก Activation Track เดียว
// เวอร์ชันนี้ป้องกันปัญหา "drift สะสม" โดยจำ offset ของเฟรมก่อนหน้าไว้แล้วหักออกก่อน
// ทำให้สั่นรอบตำแหน่งฐานที่ถูกต้องเสมอ ไม่ไหลสะสม และไม่ทับอนิเมชั่นจาก Animation Track
//
// วิธีใช้:
// 1. สร้าง Empty GameObject ชื่ออะไรก็ได้ เช่น "ShakeController"
// 2. ติดสคริปต์นี้กับ ShakeController
// 3. ลากวัตถุที่อยากให้สั่นทั้งหมดใส่ในลิสต์ "Targets" ใน Inspector (จะกี่ชิ้นก็ได้)
// 4. Timeline: Add Activation Track -> ลาก ShakeController ใส่เป็น binding -> สร้างคลิปตามช่วงเวลาที่ต้องการ

using UnityEngine;
using System.Collections.Generic;

public class MultiShake : MonoBehaviour
{
    [Header("วัตถุทั้งหมดที่อยากให้สั่นพร้อมกัน")]
    [SerializeField] private List<Transform> targets = new List<Transform>();

    [Header("ความแรง/ความเร็วการสั่น (ใช้ค่าเดียวกันทุกชิ้น)")]
    [SerializeField] private float amplitude = 0.1f;
    [SerializeField] private float frequency = 25f;
    [SerializeField] private bool shakeRotation = false;
    [SerializeField] private float rotationAmplitude = 2f;

    private float[] seedX, seedY, seedZ;

    // เก็บ offset ที่เรา "ใส่เอง" ในเฟรมก่อนหน้า เพื่อหักออกก่อนคำนวณเฟรมนี้
    // นี่คือกุญแจสำคัญที่ป้องกันไม่ให้ shake สะสม (random walk) จนไหลไปเรื่อยๆ
    private Vector3[] lastAppliedPosOffset;
    private Quaternion[] lastAppliedRotOffset;

    private void OnEnable()
    {
        int count = targets.Count;
        seedX = new float[count];
        seedY = new float[count];
        seedZ = new float[count];
        lastAppliedPosOffset = new Vector3[count];
        lastAppliedRotOffset = new Quaternion[count];

        for (int i = 0; i < count; i++)
        {
            seedX[i] = Random.Range(0f, 100f);
            seedY[i] = Random.Range(0f, 100f);
            seedZ[i] = Random.Range(0f, 100f);
            lastAppliedPosOffset[i] = Vector3.zero;
            lastAppliedRotOffset[i] = Quaternion.identity;
        }
    }

    private void LateUpdate()
    {
        float t = Time.time * frequency;

        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] == null) continue;

            // ---- ตำแหน่ง ----
            // หัก offset ที่ใส่เองในเฟรมก่อนออกก่อน เพื่อกู้ตำแหน่ง "ฐานที่สะอาด"
            // (ตำแหน่งจริงจาก Animation Track หรือค่าที่ตั้งไว้ตอนแรก) กลับคืนมา
            Vector3 basePos = targets[i].localPosition - lastAppliedPosOffset[i];

            float offsetX = (Mathf.PerlinNoise(seedX[i], t) - 0.5f) * 2f * amplitude;
            float offsetY = (Mathf.PerlinNoise(seedY[i], t) - 0.5f) * 2f * amplitude;
            float offsetZ = (Mathf.PerlinNoise(seedZ[i], t) - 0.5f) * 2f * amplitude;
            Vector3 newPosOffset = new Vector3(offsetX, offsetY, offsetZ);

            targets[i].localPosition = basePos + newPosOffset;
            lastAppliedPosOffset[i] = newPosOffset;

            // ---- การหมุน ----
            if (shakeRotation)
            {
                Quaternion baseRot = targets[i].localRotation * Quaternion.Inverse(lastAppliedRotOffset[i]);

                float rotX = (Mathf.PerlinNoise(seedX[i], t + 50f) - 0.5f) * 2f * rotationAmplitude;
                float rotY = (Mathf.PerlinNoise(seedY[i], t + 50f) - 0.5f) * 2f * rotationAmplitude;
                float rotZ = (Mathf.PerlinNoise(seedZ[i], t + 50f) - 0.5f) * 2f * rotationAmplitude;
                Quaternion newRotOffset = Quaternion.Euler(rotX, rotY, rotZ);

                targets[i].localRotation = baseRot * newRotOffset;
                lastAppliedRotOffset[i] = newRotOffset;
            }
        }
    }

    private void OnDisable()
    {
        // คืนค่า offset ล่าสุดออก เพื่อให้ตำแหน่งกลับเป็นค่าฐานที่สะอาด (ไม่มี shake ค้าง)
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] == null) continue;
            targets[i].localPosition -= lastAppliedPosOffset[i];
            if (shakeRotation)
                targets[i].localRotation = targets[i].localRotation * Quaternion.Inverse(lastAppliedRotOffset[i]);

            lastAppliedPosOffset[i] = Vector3.zero;
            lastAppliedRotOffset[i] = Quaternion.identity;
        }
    }
}