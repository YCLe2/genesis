// 초기 맵 셋업용

using UnityEngine;
using System.IO;
using System.Collections.Generic;


// JSON 저장을 위한 데이터 구조체
[System.Serializable]
public class AssetData {
    public string name;
    public float x;
    public float z;
}

[System.Serializable]
public class AssetList {
    public List<AssetData> items = new List<AssetData>();
}

public class SavePositionToJson : MonoBehaviour
{
    [Tooltip("웹 대시보드에 띄울 캐릭터나 가구 오브젝트들을 여기에 끌어다 넣으세요.")]
    public List<GameObject> targetObjects;
    
    [Tooltip("저장될 파일 이름입니다.")]
    public string fileName = "transforms.json";

    // 유니티 에디터에서 컴포넌트를 우클릭하여 실행할 수 있는 메뉴 생성
    [ContextMenu("Save Positions Now")]
    public void SaveToJson()
    {
        AssetList list = new AssetList();

        // 등록된 오브젝트들의 이름과 (X, Z) 좌표를 수집
        foreach(GameObject obj in targetObjects) {
            if (obj == null) continue;
            list.items.Add(new AssetData {
                name = obj.name,
                x = obj.transform.position.x,
                z = obj.transform.position.z // 유니티의 Z축이 웹 맵의 2D 평면 Y축이 됩니다.
            });
        }

        // JSON 텍스트로 변환
        string json = JsonUtility.ToJson(list, true);
        
        // Assets 폴더 바깥(프로젝트 루트 폴더)에 저장합니다.
        string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, fileName);
        File.WriteAllText(path, json);
        
        Debug.Log($"[Export] 총 {list.items.Count}개의 에셋 위치가 저장되었습니다!\n경로: {path}");
    }
}