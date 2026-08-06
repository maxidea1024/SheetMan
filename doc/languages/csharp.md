# C# / Unity

> [언어별 가이드로](readme.md) · [문서 목록으로](../../readme.md)

---

## 무엇이 생성되는가

```
<Path>/
  <AccessorName>.cs        접근자 — 테이블 프로퍼티, ReadAllAsync, 참조 연결
  SheetManBinaryReader.cs  바이너리 리더 (함께 생성됩니다)
  SheetManHelpers.cs       예외 타입과 보조 함수
  SheetManUpdater.cs       데이터 갱신 (WriteUpdater를 켰을 때만)
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
      "WriteUpdater": false,            // CDN에서 데이터를 갱신할 거라면 true
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

## 데이터만 갱신하기 (`WriteUpdater`)

recipe에 `"WriteUpdater": true`를 적으면 `SheetManUpdater.cs`가 함께 나옵니다. CDN이나 버킷에 올려둔 데이터를 받아 로컬 사본을 최신으로 유지하는 코드이고, **빌드를 새로 내보내지 않고 데이터만 패치**하기 위한 것입니다. 기본값이 `false`인 이유는 네트워크를 쓰기 때문이고, 데이터를 빌드 안에 넣어 배포한다면 필요가 없기 때문입니다.

익스포터가 데이터 옆에 이미 쓰고 있는 **매니페스트**(`manifest-binary.json` — 파일별 크기와 MD5)가 전부입니다. 서버에는 익스포트 결과를 그대로 올리면 되고, 따로 준비할 것이 없습니다.

```csharp
var result = await SheetManUpdater.UpdateAsync("https://cdn.example.com/data");

if (!result.Succeeded)
{
    // 이전 데이터는 그대로 있습니다. 그걸로 계속 가도 됩니다.
    Debug.LogWarning($"데이터 갱신 실패: {result.Error}");
}

await GameData.ReadAllAsync(result.LocalPath);
```

업데이터는 **읽지 않습니다.** 디렉터리를 만들어 그 경로를 돌려주고, 로드는 접근자가 합니다. 둘이 서로를 모르는 편이 낫고, 받은 데이터의 스키마가 이 빌드와 달라도 [바이너리 형식](../binary-format.md)의 태그 덕에 안전하게 읽힙니다.

**무엇을 보장하나.**

|상황|결과|
|--|--|
|바뀐 것이 없음|요청 한 번(매니페스트)으로 끝. `UpToDate == true`|
|일부 파일만 바뀜|바뀐 파일만 받습니다|
|서버에서 사라진 테이블|로컬 캐시에서도 지웁니다|
|받은 파일이 손상됨|매니페스트의 MD5와 대조해 **거부**하고, 캐시는 손대지 않습니다|
|중간에 실패·강제 종료|**이전 데이터가 그대로** 남습니다. 파일은 `.staging`을 거쳐 마지막에 옮겨지고, 로컬 매니페스트는 그보다 더 나중에 쓰입니다|
|일시적 네트워크 장애|재시도합니다 — 연결 실패·408·429·5xx. 대기 시간은 두 배씩 늘어납니다|
|404|재시도하지 않습니다. 서버가 답을 한 것이고, 세 번 더 물어도 같은 답입니다|

**설정할 수 있는 것.**

```csharp
var options = new SheetManUpdateOptions
{
    ManifestFileName = "manifest-binary.json",  // JSON 익스포트라면 manifest-json.json
    MaxAttempts = 3,                            // 첫 시도 포함
    RetryDelay = TimeSpan.FromMilliseconds(500),// 재시도마다 두 배
    RequestTimeout = TimeSpan.FromSeconds(30),
    VerifyHash = true,
    Log = Debug.Log,
};

var result = await SheetManUpdater.UpdateAsync(baseUrl, cacheDirectory: null, options, cancellationToken);
```

캐시 위치를 지정하지 않으면 유니티에서는 `Application.persistentDataPath/sheetman-data`, 그 외에서는 실행 파일 옆입니다.

**예외를 던지지 않습니다.** 네트워크·디스크·손상된 파일은 전부 호출자가 다뤄야 하는 상황이지 결함이 아니고, 게임 루프 안으로 예외를 던지는 패처는 이유를 삼키는 try/catch로 감싸이게 됩니다. 실패는 `result.Error`에 문장으로 옵니다.

> 언리얼에도 같은 것이 있습니다 — [언리얼 가이드](unreal.md#데이터만-갱신하기-writeupdater).

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
