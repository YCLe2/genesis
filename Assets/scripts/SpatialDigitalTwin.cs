using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.AI; // 🎯 내비메시(NavMesh) 인공지능 길찾기/바닥 인식을 위해 필수!
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
    public float deadzoneThreshold = 0.15f; // 15cm 데드존 (현장에서 드래그로 조절 가능!)
    
    [Tooltip("NavMesh 스냅: 서버 좌표가 벽 밖으로 튀었을 때, 최대 몇 미터 반경 안에서 걸을 수 있는 바닥을 찾을지 결정합니다.")]
    public float navMeshSampleRadius = 1.5f;

    [Header("Animation & Rotation Settings")]
    [Tooltip("캐릭터가 목표 지점까지 걸어가는(보간) 속도입니다.")]
    public float lerpSpeed = 15f;
    [Tooltip("MPU 가속도 값을 캐릭터의 까딱거리는 회전 각도로 변환하는 배율입니다.")]
    public float rotationSensitivity = 30f; 

    private string syncUrl;

    void Start() {
        syncUrl = $"http://{serverIp}:{port}/api/sync";
        StartCoroutine(SyncLoop()); // 통신 무한 루프 시작
    }

    // ==========================================
    // 1. 서버 통신 루프 (0.2초 동기화)
    // ==========================================
    IEnumerator SyncLoop() {
        while (true) {
            using (UnityWebRequest request = UnityWebRequest.Get(syncUrl)) {
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success) {
                    UpdateUnityScene(request.downloadHandler.text);
                    Debug.Log("서버에서 받은 데이터: " + request.downloadHandler.text);
                } else {
                    Debug.LogWarning("[Network] 서버 연결 실패. IP 주소를 확인하세요.");
                }
            }
            // 하드웨어 송신 주기에 맞춘 가장 효율적인 0.2초(5Hz) 대기
            yield return new WaitForSeconds(0.2f); 
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
                    float accelX = tags[tagId]["accel_x"] != null ? (float)tags[tagId]["accel_x"] : 0f;
                    float accelY = tags[tagId]["accel_y"] != null ? (float)tags[tagId]["accel_y"] : 0f;

                    // 1차 목표 좌표 (높이 Y는 캐릭터의 원래 높이 유지)
                    Vector3 rawTargetPos = new Vector3(tagX, targetObj.transform.position.y, tagZ);
                    Vector3 finalTargetPos = rawTargetPos;

                    // 🎯 [필터 A] NavMesh 건물 내벽 스냅 기능
                    if (useConstraints)
                    {
                        NavMeshHit hit;
                        // 서버 좌표가 허공이나 벽 너머로 튀었더라도, navMeshSampleRadius 반경 내의 가장 가까운 '걸을 수 있는 바닥'을 찾아 그곳으로 좌표를 당겨옵니다.
                        if (NavMesh.SamplePosition(rawTargetPos, out hit, navMeshSampleRadius, NavMesh.AllAreas))
                        {
                            finalTargetPos = hit.position;
                        }
                    }

                    // 애니메이션 연동 스크립트 가져오기
                    HardwareTagFollower follower = targetObj.GetComponent<HardwareTagFollower>();

                    // 🎯 물리 버튼이 눌렸을 때만 이동 및 회전 허용
                    // if (isButtonPressed) 
                    if(true)
                    {
                        // 🎯 [필터 B] 데드존(Deadzone) 진동 상쇄 로직
                        // 현재 내 위치와 최종 목표 위치 간의 거리 차이를 계산
                        float currentMoveDelta = Vector3.Distance(targetObj.transform.position, finalTargetPos);
                        
                        // 변화량이 우리가 설정한 15cm(0.15f)보다 클 때만 움직입니다! (제자리 덜덜 떨림 완벽 방지)
                        if (currentMoveDelta > deadzoneThreshold)
                        {
                            if (follower != null) {
                                follower.UpdateHardwarePosition(finalTargetPos);
                            } else {
                                // 애니메이션이 없는 일반 사물일 경우 그냥 부드럽게 이동
                                targetObj.transform.position = Vector3.Lerp(targetObj.transform.position, finalTargetPos, Time.deltaTime * lerpSpeed);
                            }
                        }

                        // 🎯 MPU 가속도를 이용한 부드러운 기울기 적용 (보간 Lerp 사용)
                        Quaternion targetRotation = Quaternion.Euler(accelY * rotationSensitivity, 0, -accelX * rotationSensitivity);
                        targetObj.transform.rotation = Quaternion.Lerp(targetObj.transform.rotation, targetRotation, Time.deltaTime * lerpSpeed);
                    }
                    else 
                    {
                        // 버튼을 떼면 캐릭터에게 '지금 내 위치가 곧 목표 위치다'라고 전달하여 걷기 애니메이션을 즉시 정지시킴
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