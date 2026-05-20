// 애니메이션 & 이동 스크립트

using UnityEngine;

public class HardwareTagFollower : MonoBehaviour
{
    [Header("Animation & Movement")]
    [Tooltip("캐릭터의 Animator 컴포넌트를 연결하세요.")]
    public Animator animator;
    
    [Tooltip("캐릭터가 목표 지점을 향해 쫓아가는 속도입니다.")]
    public float moveSpeed = 10f; 

    private Vector3 targetPosition;

    void Start()
    {
        // 시작할 때 자신의 Animator를 자동으로 찾음.
        if (animator == null) animator = GetComponent<Animator>();
        
        // 초기 목표 위치를 현재 내 위치로 설정 (시작하자마자 튀는 것 방지)
        targetPosition = transform.position;
    }

    // SpatialDigitalTwin.cs 에서 이 함수를 호출하여 새로운 목표 좌표를 던져줌
    public void UpdateHardwarePosition(Vector3 newPos)
    {
        targetPosition = newPos;
    }

    void Update()
    {
        // 목표 위치로 부드럽게 이동 (Lerp 보간)
        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * moveSpeed);

        // 현재 위치와 목표 위치 사이의 남은 거리를 계산
        float distanceToTarget = Vector3.Distance(transform.position, targetPosition);

        // 거리에 따라 애니메이션 걷기/정지 처리
        if (animator != null)
        {
            // 남은 거리가 0.05m(5cm) 이상이면 걷는 애니메이션(1.0), 그 이하면 정지(0.0)
            // (주의: Animator 안에 'Speed'라는 Float 파라미터가 있어야 함)
            float currentSpeed = distanceToTarget > 0.05f ? 1f : 0f;
            animator.SetFloat("Speed", currentSpeed);
        }
    }
}