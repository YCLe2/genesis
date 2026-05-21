using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.AI; // 내비메시(NavMesh) 인공지능 길찾기/바닥 인식을 위해 사용
using System.Collections;
using Newtonsoft.Json.Linq; // JSON 파싱용 패키지

public class SpatialDigitalTwin : MonoBehaviour
{
    [Header("Server Connection")]
    [Tooltip("파이썬 서버(ASUS 노트북 핫스팟)의 IP 주소입니다.")]
    public string serverIp = "192.168.100.1";
    public string port = "8000";
    
    [Header("Movement & Physical Filters")]
    public bool useConstraints = true;
    
    [Tooltip("데드존: 이 거리(미터) 이하의 미세한 좌표 변화는 센서 노이즈로 간주하고 캐릭터를 제자리에 세워둡니다.")]
    public float deadzoneThreshold = 0.03f; // 3cm 데드존 (현장에서 드래그로 조절 가능)
    
    [Tooltip("NavMesh 스냅: 서버 좌표가 벽 밖으로 튀었을 때, 최대 몇 미터 반경 안에서 걸을 수 있는 바닥을 찾을지 결정합니다.")]
    public float navMeshSampleRadius = 1.5f;

    [Header("Animation & Rotation Settings")]
    [Tooltip("캐릭터가 목표 지점까지 걸어가는(보간) 속도입니다.")]
    public float lerpSpeed = 20f;
    
    // 🎯 가속도 기울기를 Y축 회전 각도로 증폭시키기 위해 다시 활성화했습니다.
    [Tooltip("센서를 기울였을 때 제자리에서 얼마나 빨리/많이 회전할지 결정하는 배율입니다.")]
    public float rotationSensitivity = 90f; 

    [Tooltip("현실 세계의 정면을 유니티에서 어느 방향인지 Y축 회전값으로 설정")]
    public float yAxis_RotationOffset = 90f;

    private string syncUrl;

    void Start() {
        syncUrl = $"http://{serverIp}:{port}/api/sync";
        StartCoroutine(SyncLoop()); // 통신 무한 루프 시작
    }

    // ==========================================
    // 1. 서버 통신 루프 
    // ==========================================
    IEnumerator SyncLoop() {
        while (true) {
            using (UnityWebRequest request = UnityWebRequest.Get(syncUrl)) {
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success) {
                    UpdateUnityScene(request.downloadHandler.text);
                } else {
                    Debug.LogWarning("[Network] 서버 연결 실패. IP 주소를 확인하세요.");
                }
            }
            // 하드웨어 송신 주기에 맞춘 가장 효율적인 0.4초(5Hz) 대기
            yield return new WaitForSeconds(0.4f); 
        }
    }

    // ==========================================
    // 2. 데이터 적용 및 2차 물리 필터링
    // ==========================================
    void UpdateUnityScene(string json) {
        try {
            JObject data = JObject.Parse(json);
            JObject mappings = data["mappings"] as JObject;
            JObject tags = data["tags"] as JObject;
            JArray assets = data["assets"] as JArray;

            if (mappings == null || tags == null || assets == null) return;

            // 매핑(연결)된 정보만 골라서 처리합니다.
            foreach (var mapping in mappings) {
                string tagId = mapping.Key;
                int assetIndex = (int)mapping.Value;
                
                if (assetIndex < 0 || assetIndex >= assets.Count) continue;

                // 유니티 씬 안에서 매핑된 캐릭터 오브젝트 찾기
                string targetName = (string)assets[assetIndex]["name"];
                GameObject targetObj = GameObject.Find(targetName);

                if (targetObj != null && tags.ContainsKey(tagId)) {
                    
                    // 상태 및 위치/기울기 데이터 추출
                    int buttonStart = (int)tags[tagId]["button_start"];
                    bool isButtonPressed = buttonStart > 0;
                    
                    float tagX = (float)tags[tagId]["x"];
                    float tagZ = (float)tags[tagId]["z"];
                    
                    // 🎯 회전에 사용할 가속도 X값(좌우 기울기)만 가져옵니다. 
                    float accelX = tags[tagId]["accel_x"] != null ? (float)tags[tagId]["accel_x"] : 0f;

                    // 1차 목표 좌표 (높이 Y는 캐릭터의 원래 높이 유지)
                    Vector3 rawTargetPos = new Vector3(tagX, targetObj.transform.position.y, tagZ);
                    Vector3 finalTargetPos = rawTargetPos;

                    // [필터 A] NavMesh 건물 내벽 스냅 기능
                    if (useConstraints)
                    {
                        NavMeshHit hit;
                        if (NavMesh.SamplePosition(rawTargetPos, out hit, navMeshSampleRadius, NavMesh.AllAreas))
                        {
                            finalTargetPos = hit.position;
                        }
                    }

                    HardwareTagFollower follower = targetObj.GetComponent<HardwareTagFollower>();

                    // 물리 버튼이 눌렸을 때만 이동 및 회전 허용
                    if(true)
                    {
                        // [필터 B] 데드존(Deadzone) 진동 상쇄 로직
                        float currentMoveDelta = Vector3.Distance(targetObj.transform.position, finalTargetPos);
                        
                        if (currentMoveDelta > deadzoneThreshold)
                        {
                            if (follower != null) {
                                follower.UpdateHardwarePosition(finalTargetPos);
                            } else {
                                targetObj.transform.position = Vector3.Lerp(targetObj.transform.position, finalTargetPos, Time.deltaTime * lerpSpeed);
                            }
                        }

                        // 🎯 [핵심 변경] X, Z축 고정 및 Y축 회전 매핑
                        // 1. 센서의 좌우 기울기(accelX)를 회전 민감도를 곱해 Y축 회전 각도로 뻥튀기합니다.
                        // (만약 센서를 왼쪽으로 기울였는데 큐브가 오른쪽으로 돈다면, accelX 대신 -accelX 를 곱해주세요)
                        float targetYaw = accelX * rotationSensitivity;

                        // 2. X축(Pitch)과 Z축(Roll)은 0으로 완벽히 고정하고, Y축(Yaw)에만 값을 넣습니다.
                        Quaternion targetRotation = Quaternion.Euler(0, yAxis_RotationOffset + targetYaw, 0);

                        // 3. 보간(Lerp)으로 부드럽게 회전 적용
                        targetObj.transform.rotation = Quaternion.Lerp(targetObj.transform.rotation, targetRotation, Time.deltaTime * lerpSpeed);
                    }
                    else 
                    {
                        if (follower != null) {
                            follower.UpdateHardwarePosition(targetObj.transform.position);
                        }
                    }
                }
            }
        } catch { 
            // JSON 파싱 에러 발생 시 유니티가 멈추는 것을 방지
        } 
    }
}