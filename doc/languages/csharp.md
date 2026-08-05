# C# / Unity

> [언어별 가이드로](readme.md) · [문서 목록으로](../../readme.md)

---

## 무엇이 생성되는가

```
<Path>/
  <AccessorName>.cs        접근자 — 테이블 프로퍼티, ReadAllAsync, 참조 연결
  SheetManBinaryReader.cs  바이너리 리더 (함께 생성됩니다)
  SheetManHelpers.cs       예외 타입과 보조 함수
  tables/<Table>Table.cs   테이블당 하나
  enums/<Enum>.cs          enum당 하나
  constants/<Set>.cs       상수 세트당 하나
```

## 필요한 것

|항목|값|
|--|--|
|C# 언어 버전|8.0 이상|
|.NET|`netstandard2.1` 이상 (또는 .NET Core 3.0+, .NET 5+)|
|Unity|2020.3 이상|
|외부 패키지|**없음.** UniTask도, Newtonsoft도 필요 없습니다|

**유니티에서 설정할 것이 없습니다.** 생성된 코드가 유니티 내장 정의(`UNITY_5_3_OR_NEWER`, `UNITY_2021_2_OR_NEWER`, `UNITY_WEBGL`)로 스스로 판별합니다. 별도의 심볼을 프로젝트에 추가할 필요가 없습니다.

## recipe 설정

```jsonc
"CodeGenerations": {
  "CSharp": [
    {
      "Path": "Assets/Scripts/Generated",
      "Namespace": "MyGame.Data",       // 비우면 전역 네임스페이스
      "AccessorName": "GameData",       // 기본값 SheetManAccessor
      "BinaryTableFileExtension": ".bytes",
      "Sweep": true,
      "TargetSide": "c"
    }
  ]
}
```

## 프로젝트에 넣기

생성 폴더를 그대로 프로젝트에 두면 끝입니다. 유니티라면 `Assets/` 아래 아무 곳이나 됩니다.

## 쓰는 법

**접근자는 정적(static)입니다.** 인스턴스를 만들지 않습니다.

```csharp
using MyGame.Data;

await GameData.ReadAllAsync(Application.streamingAssetsPath);

var sword = GameData.Item.Find(1);
if (sword != null)
{
    // 참조는 로드 후 실제 레코드로 연결되어 있습니다.
    Debug.Log($"{sword.Name} / {sword.CategoryId.Name}");
}

foreach (var row in GameData.Item.Records)
    Debug.Log(row.Name);
```

패키징 과정에서 확장자가 바뀌었다면 두 번째 인자로 넘깁니다.

```csharp
await GameData.ReadAllAsync(Application.streamingAssetsPath, ".bytes");
```

### 파일을 어디서 읽을지 바꾸기

`ReadAllBytesAsync`가 교체 가능한 델리게이트입니다. 팩 파일, CDN, Addressables 등에서 읽으려면 `ReadAllAsync`를 부르기 **전에** 자기 것을 넣으세요.

```csharp
GameData.ReadAllBytesAsync = async filename =>
{
    var handle = Addressables.LoadAssetAsync<TextAsset>(filename);
    var asset = await handle.Task;
    return asset.bytes;
};

await GameData.ReadAllAsync("");
```

## 주의사항

**유니티 배포 경로.** 기본 구현은 컴파일 대상 플랫폼에 맞게 고릅니다. StreamingAssets은 어느 플랫폼에서나 배포되지만, 두 곳에서는 경로가 아니라 URL입니다.

|플랫폼|`Application.streamingAssetsPath`|기본 구현이 하는 일|
|--|--|--|
|Android|`jar:file:///.../base.apk!/assets` (APK 안)|`UnityWebRequest`|
|WebGL|웹서버 URL|`UnityWebRequest`|
|그 외|실제 경로|`File.ReadAllBytesAsync`|

둘 다 `"://"`를 포함하므로 한 번의 검사로 갈립니다. `persistentDataPath`는 어디서나 실제 경로라 파일 API로 갑니다.

**WebGL에는 스레드가 없습니다.** WebGL 빌드에서는 `File.ReadAllBytes`를 동기로 부릅니다 — `Task.Run`이 동작하지 않기 때문입니다. 에디터에서는 그렇지 않으므로 `UNITY_WEBGL && !UNITY_EDITOR`로 갈립니다.

**확장자.** 유니티는 `.bytes`인 파일만 TextAsset으로 포함합니다. `Resources/`나 Addressables로 배포한다면 recipe에서 `"BinaryTableFileExtension": ".bytes"`로 두세요. StreamingAssets은 확장자를 가리지 않으므로 `.table` 그대로도 됩니다.

## 트러블슈팅

|증상|원인과 조치|
|--|--|
|안드로이드에서만 "파일 없음"|StreamingAssets이 APK 안이라 파일 API로 못 읽습니다. 기본 구현은 처리하지만, `ReadAllBytesAsync`를 직접 교체했다면 URL 경로를 함께 처리해야 합니다|
|WebGL에서 멈춤|`ReadAllBytesAsync`를 교체하면서 스레드를 쓰는 코드를 넣었는지 확인하세요|
|참조가 `null`|테이블 하나만 읽었기 때문입니다. 참조 연결은 `ReadAllAsync`가 전부 읽은 뒤에 일어납니다|
|삭제한 테이블의 클래스가 남아 있음|`"Sweep": false`로 꺼두었을 때만 그렇습니다. 켜져 있으면 생성 파일 중 이번에 쓰지 않은 것은 지워집니다|
|`TextAsset`으로 잡히지 않음|확장자를 `.bytes`로 바꾸세요 (위 「확장자」)|
