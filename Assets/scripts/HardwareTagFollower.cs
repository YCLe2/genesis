// 큐브용 이동 스크립트 (애니메이션, Y축 이동 제거)

using UnityEngine;

public class HardwareTagFollower : MonoBehaviour
{
    [Header("Movement Settings")]
    [Tooltip("큐브가 목표 지점을 향해 이동하는 속도입니다.")]
    public float moveSpeed = 10f; 

    [Header("Debug Monitor")]
    [Tooltip("서버에서 실시간으로 받아온 목표 좌표입니다.")]
    [SerializeField] private Vector3 targetPosition; // 인스펙터 노출용

    void Start()
    {
        // 초기 목표 위치를 큐브의 현재 위치로 설정 (시작 시 순간이동 방지)
        targetPosition = transform.position;
    }

    // SpatialDigitalTwin.cs 에서 이 함수를 호출하여 새로운 목표 좌표를 던져줌
    public void UpdateHardwarePosition(Vector3 newPos)
    {
        targetPosition = newPos;
    }

    void Update()
    {
        // 🎯 핵심 수정: 목표 좌표의 X, Z만 가져오고, Y는 큐브의 현재 높이를 그대로 유지합니다.
        Vector3 flatTargetPosition = new Vector3(targetPosition.x, transform.position.y, targetPosition.z);

        // 큐브를 평면 목표 위치로 부드럽게 이동 (Lerp 보간)
        transform.position = Vector3.Lerp(transform.position, flatTargetPosition, Time.deltaTime * moveSpeed);
    }
}