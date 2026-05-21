using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic; // 🎯 Dictionary 사용을 위해 추가
using Newtonsoft.Json.Linq; 

public class SpatialDigitalTwin : MonoBehaviour
{
    [Header("Server Connection")]
    public string serverIp = "192.168.100.1";
    public string port = "8000";
    
    [Header("Movement & Physical Filters")]
    public bool useConstraints = true;
    public float deadzoneThreshold = 0.03f; 
    public float navMeshSampleRadius = 1.5f;

    [Header("Animation & Rotation Settings")]
    public float lerpSpeed = 20f;
    
    [Tooltip("현실 세계의 정면을 유니티에서 어느 방향인지 Y축 회전값으로 설정")]
    public float yAxis_RotationOffset = 90f;

    // 🎯 [새로 추가됨] 각 태그별로 '현재까지 회전한 총 각도'를 저장하는 딕셔너리
    private Dictionary<string, float> accumulatedYawAngles = new Dictionary<string, float>();

    private string syncUrl;

    void Start() {
        syncUrl = $"http://{serverIp}:{port}/api/sync";
        StartCoroutine(SyncLoop()); 
    }

    IEnumerator SyncLoop() {
        while (true) {
            using (UnityWebRequest request = UnityWebRequest.Get(syncUrl)) {
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success) {
                    UpdateUnityScene(request.downloadHandler.text);

                    Debug.Log("데이터: " + request.downloadHandler.text);
                } else {
                    Debug.LogWarning("[Network] 서버 연결 실패. IP 주소를 확인하세요.");
                }
            }
            // 하드웨어 송신 주기에 맞춘 0.4초 대기
            yield return new WaitForSeconds(0.4f); 
        }
    }

    void UpdateUnityScene(string json) {
        try {
            JObject data = JObject.Parse(json);
            JObject mappings = data["mappings"] as JObject;
            JObject tags = data["tags"] as JObject;
            JArray assets = data["assets"] as JArray;

            if (mappings == null || tags == null || assets == null) return;

            foreach (var mapping in mappings) {
                string tagId = mapping.Key;
                int assetIndex = (int)mapping.Value;
                
                if (assetIndex < 0 || assetIndex >= assets.Count) continue;

                string targetName = (string)assets[assetIndex]["name"];
                GameObject targetObj = GameObject.Find(targetName);

                if (targetObj != null && tags.ContainsKey(tagId)) {
                    
                    float tagX = (float)tags[tagId]["x"];
                    float tagZ = (float)tags[tagId]["z"];
                    
                    // 🎯 1. 자이로스코프 Z축 회전 속도(deg/s) 데이터를 가져옵니다.
                    float gyroZ = tags[tagId]["gyro_z"] != null ? (float)tags[tagId]["gyro_z"] : 0f;

                    // 1차 목표 좌표 
                    Vector3 rawTargetPos = new Vector3(tagX, targetObj.transform.position.y, tagZ);
                    Vector3 finalTargetPos = rawTargetPos;

                    if (useConstraints) {
                        NavMeshHit hit;
                        if (NavMesh.SamplePosition(rawTargetPos, out hit, navMeshSampleRadius, NavMesh.AllAreas)) {
                            finalTargetPos = hit.position;
                        }
                    }

                    HardwareTagFollower follower = targetObj.GetComponent<HardwareTagFollower>();

                    // 이동 로직
                    float currentMoveDelta = Vector3.Distance(targetObj.transform.position, finalTargetPos);
                    if (currentMoveDelta > deadzoneThreshold) {
                        if (follower != null) follower.UpdateHardwarePosition(finalTargetPos);
                        else targetObj.transform.position = Vector3.Lerp(targetObj.transform.position, finalTargetPos, Time.deltaTime * lerpSpeed);
                    } else {
                        if (follower != null) follower.UpdateHardwarePosition(targetObj.transform.position);
                    }

                    // =========================================================
                    // 🎯 2. 자이로스코프를 이용한 Y축 완벽 회전 로직
                    // =========================================================
                    
                    // a) 초기화: 이 태그의 회전 기록이 없다면 0도로 시작
                    if (!accumulatedYawAngles.ContainsKey(tagId)) {
                        accumulatedYawAngles[tagId] = 0f;
                    }

                    // b) 노이즈 필터링 (데드존): 가만히 있어도 센서 떨림 때문에 값이 조금씩 누적되어 혼자 빙글빙글 도는 현상(Drift) 방지
                    // 초당 2도 미만의 미세한 떨림은 회전하지 않은 것(0)으로 간주합니다.
                    if (Mathf.Abs(gyroZ) < 2.0f) {
                        gyroZ = 0f;
                    }

                    // c) 각도 누적 (적분): 회전 속도 * 시간(0.4초 주기)
                    // 현재 아두이노가 0.4초(400ms)마다 데이터를 쏘고 있으므로 0.4를 곱해줍니다.
                    accumulatedYawAngles[tagId] += (gyroZ * 0.4f); 

                    // d) 최종 회전 적용: X, Z축은 0으로 고정하고 누적된 Y축 각도만 적용
                    Quaternion targetRotation = Quaternion.Euler(0, yAxis_RotationOffset + accumulatedYawAngles[tagId], 0);
                    targetObj.transform.rotation = Quaternion.Lerp(targetObj.transform.rotation, targetRotation, Time.deltaTime * lerpSpeed);
                }
            }
        } catch { } 
    }
}