using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using UnityEngine.Networking;
using SheetMan.Runtime;
using UnityEngine.UI;
using StaticData;
using System.IO;

public class Runner : MonoBehaviour
{
    public string remoteBaseUrl;
    public Text statusText;

    async UniTaskVoid Start()
    {
        statusText.text = "테이블 데이터 업데이트 여부를 확인중입니다.";

        var error = await SheetManUpdater.UpdateAsync(remoteBaseUrl);
        if (!string.IsNullOrEmpty(error))
        {
            Debug.LogError($"최신 데이터로 업데이트할 수 없습니다. error={error}");
        }


        await UniTask.Delay(1000);


        statusText.text = "테이블 데이터를 불러오고 있습니다.";

        await Tables.ReadAllAsync(SheetManUpdater.LocalBasePath);


        statusText.text = "데이터를 불러왔습니다!";

        foreach (var record in Tables.Localization.Records)
            Debug.Log(record.ToString());
    }
}
