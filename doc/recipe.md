# Recipe 파일

무엇을 어디서 읽어 어디로 내보낼지 적는 파일.

> [문서 목록으로](../readme.md)

---

## Recipe 파일 작성

`recipe` 파일은 입력 소스와 출력 대상을 지정하는 `.json` 파일입니다. `//` 주석을 사용할 수 있습니다.

`sheetman --new-recipe myrecipe.json` 으로 시작용 recipe를 만들 수 있습니다. 모든 목록에 기본값이 채워진 항목 하나가 들어 있고, 파일 머리에 사용 가능한 소스/타깃 이름이 적혀 나옵니다. 그대로 실행해도 아무것도 만들지 않고 정상 종료합니다 — 경로가 비어 있으면 꺼진 것으로 취급되기 때문입니다.

### 공통 설정

|키|기본값|설명|
|--|--|--|
|`ArrayDelimiter`|`";"`|배열 셀의 요소 구분자. 정확히 한 글자여야 합니다.|

### `Sources` — 무엇을 읽을지

읽을 곳은 두 가지이고, 여러 개를 함께 둘 수 있습니다. 전부 합쳐서 하나의 모델이 됩니다.

```jsonc
"Sources": {
  "Xlsx": [
    { "Path": "./sheets", "FileExtensionPatterns": ".xls;.xlsx" }
  ],
  "GoogleSheets": [
    { "ClientSecretFilename": "./client-secret.json", "SheetsId": "10NXZ..." }
  ]
}
```

|키|어디에|기본값|설명|
|--|--|--|--|
|`Path`|Xlsx|—|워크북을 찾을 폴더. 하위 폴더까지 봅니다. 이름이 `#`으로 시작하는 파일·폴더는 건너뜁니다.|
|`FileExtensionPatterns`|Xlsx|`.xls;.xlsx`|주워올 확장자. `;`로 구분합니다.|
|`ClientSecretFilename`|GoogleSheets|—|OAuth 클라이언트 비밀 파일 경로. **커밋하지 마세요.**|
|`SheetsId`|GoogleSheets|—|문서 URL에 들어 있는 긴 식별자.|

아래 넷은 **두 소스 모두** 같습니다.

|키|기본값|설명|
|--|--|--|
|`Layout`|`"sheetman"`|시트를 읽는 방식. [아래](#layout--시트를-읽는-방식) 참고.|
|`IncludeSheets`|`[]`(전부)|읽을 시트 이름. 배열 또는 `;`로 이은 문자열. `*` `?` 와일드카드.|
|`ExcludeSheets`|`[]`|제외할 시트. `IncludeSheets` 다음에 적용됩니다.|
|`OnDuplicateIndex`|`"error"`|인덱스 값이 겹칠 때. `rescue` 레이아웃 전용.|

#### `Layout` — 시트를 읽는 방식

셀 격자를 **어떻게 해석할지**를 고르는 설정입니다. 어디서 읽어오는지(엑셀이냐 구글시트냐)와는 무관하므로, 두 소스 모두에서 쓸 수 있습니다.

|값|무엇인가|
|--|--|
|`sheetman`|**기본값.** `~~table:Item~~` 같은 마커로 엔티티를 선언합니다. 한 시트에 여러 개를 아무 데나 놓을 수 있습니다.|
|`rescue`|마커 없이 **시트 탭 이름이 곧 테이블**이고 머리 3줄이 헤더인 형태. 다른 방식으로 만들어져 이미 운영 중인 엑셀을 그대로 읽기 위한 것입니다.|

**소스 항목마다 따로 지정하므로 한 번에 섞어 읽을 수 있습니다.** 옮기는 도중에 쓰라고 그렇게 만들었습니다 — 한쪽 워크북의 테이블이 다른 쪽에서 선언한 enum을 타입으로 써도 됩니다.

```jsonc
"Xlsx": [
  { "Path": "./sheets",        "Layout": "sheetman" },   // 이미 옮긴 것
  { "Path": "./legacy-sheets", "Layout": "rescue"   }    // 아직 안 옮긴 것
]
```

각 레이아웃이 시트를 어떻게 읽는지는 [시트 작성](sheets.md#다른-레이아웃-읽기--rescue)에 있습니다. 실제 프로젝트를 통째로 읽어본 기록은 [기존 프로젝트 읽어오기](adopting-a-project.md)에 있습니다.

#### `IncludeSheets` / `ExcludeSheets` — 읽을 시트 골라내기

기본은 **전부 포함**입니다. 워크북에 데이터 외의 것(참고용 탭, 작업 메모, 만들다 만 표)이 섞여 있다면 골라낼 수 있습니다.

```jsonc
{
  "Path": "./sheets",
  "IncludeSheets": ["CharacterTable", "ItemTable", "Stage*"],
  "ExcludeSheets": "*참고용*"
}
```

목록이 길면 배열이, 하나면 문자열이 읽기 좋습니다. **`IncludeSheets`에 적었는데 없는 시트는 오류입니다** — 적어놓고 조용히 빠지면 산출물에서 테이블 하나가 사라진 걸 아무도 모르기 때문이고, 오류 메시지가 실제로 있는 시트 목록을 같이 보여줍니다.

#### `OnDuplicateIndex` — 인덱스가 겹칠 때

|값|무엇을 하나|
|--|--|
|`error`|**기본값.** 겹친 값을 전부 모아 보고하고 멈춥니다. 인덱스가 존재하는 이유 자체입니다.|
|`keep-first`|먼저 나온 행을 남기고 뒤를 버립니다.|
|`keep-last`|나중 행이 앞을 덮어씁니다.|

뒤의 둘은 **수년째 돌아가던 워크북을 넘겨받는 상황**을 위한 것이고, `rescue` 레이아웃에서만 동작합니다. 버린 행을 전부 로그에 남기므로 선택이 recipe에만 적혀 있고 끝나지 않습니다.

### 출력 항목 공통 설정

모든 출력 항목(`Exports`, `CodeGenerations`)은 아래를 지원합니다.

|키|기본값|설명|
|--|--|--|
|`TargetSide`|`"cs"`|이 출력이 어느 쪽을 위한 것인지. `"c"`(클라), `"s"`(서버), `"cs"`(양쪽). 반대쪽으로 지정된 엔티티와 필드가 제외됩니다.|

> 익스포터와 그 파일을 읽는 코드 제너레이터는 **같은 `TargetSide`로 맞춰야** 합니다. 컬럼 집합이 어긋나면 생성된 리더가 데이터와 맞지 않습니다.

서버/클라 각각을 뽑으려면 항목을 두 개 두고 각기 다른 `TargetSide`와 경로를 지정하면 됩니다.

### `Targets` — 이름으로 지정하는 출력 항목

출력 항목을 섹션에 넣는 대신 `Type`으로 타깃을 지목할 수도 있습니다.

```json
"Targets": [
  { "Type": "binary", "Path": "./out/data", "FileExtension": ".table" },
  { "Type": "csharp", "Path": "./out/cs", "Namespace": "MyGame.Data", "AccessorName": "GameData" }
]
```

`Type` 외의 필드는 그 타깃의 설정이며, 전용 섹션에 쓰는 것과 동일합니다. 등록된 타깃은 모두 여기서 쓸 수 있으니 두 방식을 섞어도 됩니다.

|`Type`|종류|
|--|--|
|`binary`, `json`|파일 내보내기|
|`mysql`, `postgresql`, `mongodb`, `redis`|데이터베이스 내보내기|
|`cpp`, `csharp`, `typescript`, `html`|코드 생성 — 설정은 [언어별 가이드](languages/readme.md)|
|`c`, `go`, `rust`, `python`, `java`, `kotlin`, `ruby`, `php`, `dart`|코드 생성 (전용 섹션 없음 — `Targets`로만 지정)|
|`unreal`|Unreal 모듈 생성 (`Targets`로만 지정)|
|`summary`, `history`|변환 자체를 기록 (`Targets`로만 지정) — 「[Summary와 히스토리](history.md)」|

두 방식이 있는 이유는 타깃을 추가할 때 recipe 스키마를 고치지 않아도 되게 하기 위함입니다. 위 섹션들은 `Targets`보다 먼저 있었고 기존 recipe를 위해 남아 있습니다.

- 없는 `Type`은 **오류**입니다. 출력을 요청했는데 조용히 아무것도 안 나오면, 있어야 할 파일이 빠진 채 빌드가 나갑니다.
- 그 타깃에 없는 필드도 **오류**입니다. `FileExtention`처럼 오타를 내면 기본값으로 조용히 넘어가고, 증상은 "설정이 안 먹는다"로만 보입니다.

---

## 시작점 고르기

백지에서 시작할 필요가 없습니다. `--template`이 상황에 맞는 recipe를 내놓고, **설정마다 무엇을 위한 것이고 언제 바꾸는지 주석이 붙어 있습니다.**

```
sheetman --new-recipe my-recipe.json --template unity
```

|템플릿|무엇을 위한 것|들어 있는 것|
|--|--|--|
|`unity`|유니티 클라이언트|엑셀 → StreamingAssets(`.bytes`) + C# + HTML 문서|
|`client-server`|같은 시트에서 두 벌|`TargetSide`로 가른 바이너리 두 개, C#(클라)과 Go(서버)|
|`web`|브라우저|구글 스프레드시트 → JSON + 바이너리 + TypeScript + HTML|
|`server`|게임 서버|바이너리 + MySQL 적재 + C++|
|`unreal`|언리얼|바이너리 + 모듈 하나. 패키징 주의사항이 주석에 있습니다|
|`ci`|빌드 파이프라인|바이너리 + summary + 셀 단위 히스토리|

`--template`을 **생략하면** 모든 섹션이 기본값 항목 하나씩을 담은 파일이 나옵니다. 무엇을 쓸 수 있는지 훑어보기에는 그쪽이 낫습니다 — 다만 마흔 개의 기본값이 늘어선 파일도 그 나름의 백지라, 실제로 시작할 때는 템플릿 쪽이 빠릅니다.

> 템플릿은 회귀 스위트가 **실제로 변환해봅니다.** 설정 이름이 바뀌면 변환이 거부하므로, 낡은 템플릿은 테스트가 깨져서 드러납니다.

---

## 설정 하나하나

### 어디에나 있는 것

|키|기본값|무엇인가|
|--|--|--|
|`Path`|`""`|**출력이 나갈 디렉터리.** 없으면 만듭니다. 상대 경로는 **CLI를 실행한 위치** 기준입니다 — recipe 파일 위치가 아닙니다. **비워두면 그 항목은 꺼진 것으로 취급**되어 아무것도 만들지 않습니다. recipe에서 항목을 지우지 않고 잠시 끌 때 쓰면 됩니다.|
|`TargetSide`|`"cs"`|**이 출력이 어느 쪽 빌드를 위한 것인가.** `"c"`는 클라이언트, `"s"`는 서버, `"cs"`(또는 빈 값)는 양쪽. 반대쪽으로 표시된 엔티티와 필드가 이 출력에서 빠집니다. 클라이언트 빌드에 서버 전용 테이블을 보내지 않기 위한 것입니다.|
|`Sweep`|`true`|**지난 실행의 잔재를 지울 것인가.** 시트에서 테이블을 지우면 그 파일이 남는데, 남은 파일은 없는 타입을 이름 부르므로 지저분하거나 컴파일을 깨뜨립니다. 지워지는 것은 **헤더에 `Generated by SheetMan`이 적힌 파일 중 이번 실행이 쓰지 않은 것**뿐이라, 남의 소스가 든 폴더를 가리켜도 안전합니다. 생성물을 손으로 고쳐 쓴다면 `false`로 두세요.|
|`BinaryTableFileExtension`|`".table"`|**생성된 리더가 찾을 데이터 파일의 확장자.** 익스포터의 `FileExtension`과 **반드시 같아야** 합니다 — 다르면 리더가 파일을 못 찾습니다. 유니티에 넣는다면 `.bytes`가 필요할 수 있습니다.|

> `Path`가 비면 꺼짐, `Sweep`은 마커가 있는 파일만, 확장자는 익스포터와 짝. 이 셋이 실제로 가장 많이 어긋나는 지점입니다.

### 이름과 관련된 것

이름 설정은 언어마다 다른 것을 가리킵니다. **`AccessorName`은 대체로 "전부 담고 있는 진입점의 이름"** 이고, 어디에 쓰이는지가 언어마다 다릅니다.

|키|해당 언어|무엇의 이름인가|기본값|
|--|--|--|--|
|`AccessorName`|C#, C++, TypeScript|접근자 클래스와 그 파일. 나머지 타입은 자기 이름의 파일로 옆에 놓입니다|`SheetManAccessor`|
|`AccessorName`|C|접근자이자 **모든 타입·함수 이름의 접두사**. C에는 네임스페이스가 없어 이것이 충돌 회피의 전부입니다 — `GameData`면 `GameData_ItemRecord_t`, `GameData_ItemLoad`|`SheetManData`|
|`AccessorName`|Java, Kotlin, PHP|접근자 클래스(Kotlin은 `object`)와 그 파일|`SheetManData`|
|`AccessorName`|Go, Ruby, Dart|생성 **파일의 이름**(확장자 제외). 타입 이름이 아닙니다|`sheetman_data`|
|`AccessorName`|Unreal|접근자 클래스와 헤더·`.cpp`의 이름. 관례상 `F`로 시작합니다|`FSheetManData`|
|`Namespace`|C#, C++, TypeScript|생성 코드를 감쌀 네임스페이스. **비우면 전역**이라 다른 코드와 이름이 부딪힐 수 있습니다|`""`|
|`Namespace`|PHP|생성 파일이 선언할 네임스페이스|`GameData`|
|`PackageName`|Go|생성 파일이 선언할 Go 패키지|`gamedata`|
|`PackageName`|Java, Kotlin|생성 코드의 패키지. `Path` **아래에 폴더로 펼쳐집니다** (`com.a.b` → `com/a/b/`)|`gamedata`|
|`PackageName`|Python|생성 패키지의 이름이자 폴더 이름이자 `import`할 이름|`gamedata`|
|`ModuleName`|Python|접근자가 들어갈 모듈 (`tables.py`). `PackageName`과 **다르게** 두세요|`tables`|
|`ModuleName`|Ruby|생성 타입 전부를 감쌀 모듈|`GameData`|
|`ModuleName`|Unreal|모듈 이름. 디렉터리·`Build.cs`·export 매크로의 이름이고, 다른 모듈이 의존성으로 적을 이름입니다|`SheetManData`|
|`CrateName`|Rust|`Cargo.toml`이 선언할 크레이트 이름. 소비자가 타입을 부를 때 쓰는 이름이기도 합니다|`gamedata`|
|`ModulePath`|Go|`go.mod`가 선언할 모듈 경로이자, 생성 파일이 리더를 import할 접두사. Go에는 상대 import가 없어 필요합니다|`gamedata`|

### 언어별로만 있는 것

|키|해당 언어|기본값|무엇인가|
|--|--|--|--|
|`WriteGoMod`|Go|`true`|`go.mod`를 함께 쓸 것인가. 이미 있는 모듈 안에 넣는다면 `false`|
|`GoVersion`|Go|`"1.21"`|생성되는 `go.mod`가 요구할 Go 버전|
|`WriteCargoToml`|Rust|`true`|`Cargo.toml`을 함께 쓸 것인가. 이미 있는 크레이트 안에 넣는다면 `false`|
|`Edition`|Rust|`"2021"`|생성되는 `Cargo.toml`이 선언할 edition|
|`WriteBuildFile`|Unreal|`true`|모듈의 `Build.cs`를 쓸 것인가. 의존성을 직접 관리한다면 `false`|
|`UseStringEnum`|TypeScript|`false`|enum을 숫자 대신 문자열 유니온으로. 디버거와 로그에서 읽히지만 파일에 저장된 정수와는 어긋납니다|
|`WriteUpdater`|전부|`false`|데이터 갱신기를 리더 옆에 함께 낼 것인가. CDN에서 바뀐 파일만 받아 로컬 사본을 최신으로 유지합니다. 유일하게 네트워크를 쓰는 생성물이라 기본값이 `false`이고, **의존성이 생기는 유일한 자리**이기도 합니다 — 언리얼은 `Build.cs`에 `HTTP` 모듈이, Rust는 `Cargo.toml`에 `ureq`가 함께 들어갑니다. 나머지 언어는 표준 라이브러리만 씁니다. 「[C#](languages/csharp.md#데이터만-갱신하기-writeupdater)」·「[언리얼](languages/unreal.md#데이터만-갱신하기-writeupdater)」·「[Rust](languages/rust.md#데이터만-갱신하기-writeupdater)」·「[Ruby](languages/ruby.md#데이터만-갱신하기-writeupdater)」|

### 내보내기

|키|해당|기본값|무엇인가|
|--|--|--|--|
|`FileExtension`|Binary|`".table"`|각 테이블 파일의 확장자. 코드 생성 쪽 `BinaryTableFileExtension`과 짝을 맞추세요|
|`Compress`|Binary|`false`|**예약. 구현되어 있지 않습니다.** 형식이 압축 플래그 자리를 비워두고 있을 뿐, 아무것도 읽거나 쓰지 않습니다|
|`SchemaBaseline`|Binary|`""`|지난 스키마의 기록을 둘 경로. **커밋하세요.** 매 실행이 스키마를 그것과 비교해서, 이미 배포된 리더가 버티지 못할 변경이면 **아무것도 쓰기 전에** 컬럼 이름을 짚어 멈춥니다. 비워두면 검사하지 않습니다|
|`AcceptSchemaChanges`|Binary|`[]`|의도한 변경을 `"테이블.컬럼"`으로 승인. 타입 변경은 재생성된 코드와 함께 나가야 하므로 자동 통과가 아닙니다. 한 번 통과하면 베이스라인이 갱신되니 다시 지워도 됩니다|
|`UseCompactRowFormat`|Json|`false`|각 행을 필드 이름 있는 객체 대신 **값만 담은 배열**로. 작아지지만 사람이 보기 어렵습니다|
|`Indented`|Json|`false`|들여쓰기. 사람이 들여다볼 때만 켜세요|
|`ConnectionString`|DB 4종|`""`|연결 문자열. **`${NAME}`으로 환경 변수를 채웁니다** — 비밀번호를 recipe에 적지 마세요. 변수가 없으면 오류이고 어느 변수인지 말합니다|
|`NamePrefix`|DB 4종|`""`|기록되는 모든 테이블·컬렉션·키 이름의 접두사. 데이터베이스 하나에 독립된 데이터 세트를 여럿 둘 때|

### 기록

|키|해당|기본값|무엇인가|
|--|--|--|--|
|`FileName`|Summary|`"summary.json"`|문서의 파일 이름|
|`ConnectionString`|History|`""`|히스토리가 사는 곳. `${NAME}` 지원|
|`ProjectKey`|History|`""`|어느 프로젝트의 히스토리인가. 데이터베이스 하나가 여럿을 담을 수 있고, **이 값을 바꾸면 이어지는 게 아니라 새 히스토리가 시작됩니다**|
|`RecordDirty`|History|`false`|커밋되지 않은 변경이 있는 워킹카피의 변환도 기록할 것인가. 꺼져 있는 이유는 그런 변환이 어느 커밋에도 없는 작업을 담고 있기 때문입니다|
|`AllowOutOfOrder`|History|`false`|브랜치가 이미 도달한 것보다 오래된 커밋도 기록할 것인가|
|`OnFailure`|History|`"warn"`|히스토리에 닿지 못했을 때 `warn`할지 `fail`할지. 기본이 `warn`인 이유는 빌드의 본업이 게임 데이터를 만드는 것이고, 기록용 데이터베이스가 잠깐 안 된다고 그것을 멈출 이유가 없기 때문입니다|


---

## 예제

상황별로 하나씩. 그대로 두고 경로만 바꾸면 됩니다.

### 1. 가장 작은 것 — 엑셀 하나에서 C#으로

```jsonc
{
  "Sources": {
    "Xlsx": [ { "Path": "./sheets", "FileExtensionPatterns": ".xlsx" } ]
  },

  "Exports": {
    "Binary": [ { "Path": "./generated/data" } ]
  },

  "CodeGenerations": {
    "CSharp": [ { "Path": "./generated/cs", "Namespace": "MyGame.Data", "AccessorName": "GameData" } ]
  }
}
```

`sheets/`의 워크북을 읽어 `generated/data/<테이블>.table`과 `generated/cs/`의 C# 코드를 냅니다.

### 2. 유니티 클라이언트

확장자가 `.bytes`인 것에 주의하세요 — 유니티는 그것만 TextAsset으로 포함합니다.

```jsonc
{
  "Sources": {
    "Xlsx": [ { "Path": "./sheets", "FileExtensionPatterns": ".xlsx" } ]
  },

  "Exports": {
    "Binary": [
      {
        // StreamingAssets는 모든 플랫폼에 배포됩니다.
        "Path": "./Assets/StreamingAssets/Data",
        "FileExtension": ".bytes",
        "TargetSide": "c"
      }
    ]
  },

  "CodeGenerations": {
    "CSharp": [
      {
        "Path": "./Assets/Scripts/Generated",
        "Namespace": "MyGame.Data",
        "AccessorName": "GameData",
        "BinaryTableFileExtension": ".bytes",   // 익스포터와 짝
        "TargetSide": "c"                        // 서버 전용 데이터는 클라 빌드에 넣지 않습니다
      }
    ]
  }
}
```

### 3. 서버와 클라이언트를 함께

같은 시트에서 두 벌을 뽑습니다. **`TargetSide`가 익스포터와 코드 생성 양쪽에서 맞아야** 합니다 — 어긋나면 컬럼 집합이 달라져 리더가 데이터와 맞지 않습니다.

```jsonc
{
  "Sources": {
    "Xlsx": [ { "Path": "./sheets", "FileExtensionPatterns": ".xlsx" } ]
  },

  "Exports": {
    "Binary": [
      { "Path": "./build/client/data", "FileExtension": ".bytes", "TargetSide": "c" },
      { "Path": "./build/server/data", "TargetSide": "s" }
    ]
  },

  "CodeGenerations": {
    "CSharp": [
      {
        "Path": "./client/Assets/Scripts/Generated",
        "Namespace": "MyGame.Data", "AccessorName": "GameData",
        "BinaryTableFileExtension": ".bytes", "TargetSide": "c"
      }
    ]
  },

  "Targets": [
    {
      "Type": "go",
      "Path": "./server/internal/gamedata",
      "PackageName": "gamedata", "ModulePath": "myserver/internal/gamedata",
      "WriteGoMod": false,                      // 이미 서버 모듈 안입니다
      "TargetSide": "s"
    }
  ]
}
```

### 4. 웹 — 구글 스프레드시트에서 TypeScript로

TypeScript는 JSON과 바이너리 양쪽을 읽으므로 둘 다 내보냅니다.

```jsonc
{
  "Sources": {
    "GoogleSheets": [
      {
        // 커밋하지 마세요.
        "ClientSecretFilename": "./secrets/googlesheets-client-secret.json",
        "SheetsId": "10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU"
      }
    ]
  },

  "Exports": {
    "Json":   [ { "Path": "./public/data", "Indented": false } ],
    "Binary": [ { "Path": "./public/data" } ]
  },

  "CodeGenerations": {
    "Typescript": [ { "Path": "./src/generated", "AccessorName": "Tables" } ],
    "Html":       [ { "Path": "./docs/data" } ]
  }
}
```

### 5. 게임 서버 — 데이터베이스로 직접

비밀번호는 recipe에 적지 않습니다. `${NAME}`이 환경 변수를 채우고, 변수가 없으면 오류입니다.

```jsonc
{
  "Sources": {
    "Xlsx": [ { "Path": "./sheets", "FileExtensionPatterns": ".xlsx" } ]
  },

  "Exports": {
    "MySql": [
      {
        "ConnectionString": "Server=db;Database=game;Uid=sheetman;Pwd=${DB_PASSWORD}",
        "NamePrefix": "sm_",     // 한 데이터베이스에 여러 세트를 둘 때
        "TargetSide": "s"
      }
    ],
    "Redis": [
      {
        "ConnectionString": "${REDIS_HOST}:6379,password=${REDIS_PASSWORD}",
        "TargetSide": "s"
      }
    ]
  },

  "Targets": [
    { "Type": "cpp", "Path": "./src/generated", "Namespace": "game::data",
      "AccessorName": "GameData", "TargetSide": "s" }
  ]
}
```

### 6. 언리얼

모듈이 `Source/GameData/`에 생성됩니다. 데이터를 어디에 두고 패키징에 어떻게 포함시키는지는 [Unreal 가이드](languages/unreal.md#패키징--데이터가-빌드에-들어가는가)를 보세요.

```jsonc
{
  "Sources": {
    "Xlsx": [ { "Path": "./Sheets", "FileExtensionPatterns": ".xlsx" } ]
  },

  "Exports": {
    "Binary": [ { "Path": "./Content/Data", "TargetSide": "c" } ]
  },

  "Targets": [
    {
      "Type": "unreal",
      "Path": "./Source",
      "ModuleName": "GameData",
      "AccessorName": "FGameData",
      "TargetSide": "c"
    }
  ]
}
```

### 7. CI — 누가 무엇을 바꿨는지 기록하며

`history`는 변환마다 셀 단위 스냅샷을 남깁니다. `OnFailure`가 `warn`이라, 기록용 데이터베이스가 잠깐 안 되어도 빌드는 계속됩니다.

`SchemaBaseline`은 CI에서 특히 값을 합니다 — 이미 배포된 클라이언트가 못 읽을 스키마 변경이면 **데이터를 쓰기 전에** 빌드가 멈춥니다. 베이스라인 파일은 커밋하세요.

```jsonc
{
  "Sources": {
    "Xlsx": [ { "Path": "./sheets", "FileExtensionPatterns": ".xlsx" } ]
  },

  "Exports": {
    "Binary": [
      {
        "Path": "./build/data",
        "SchemaBaseline": "./schema-baseline.json",
        "AcceptSchemaChanges": []
      }
    ]
  },

  "Targets": [
    { "Type": "summary", "Path": "./build/summary" },
    {
      "Type": "history",
      "ConnectionString": "Server=${HISTORY_HOST};Database=sheetman;Uid=ci;Pwd=${HISTORY_PASSWORD}",
      "ProjectKey": "mygame",
      "OnFailure": "warn"
    }
  ]
}
```

```
sheetman --recipe ci-recipe.json --commit $GITHUB_SHA
```

### 8. 전부 — 13개 언어를 한 번에

`showcases/showcase.json`이 저장소에 있고, 실제로 매번 실행되어 [showcases/](../showcases/)에 결과가 커밋됩니다. 언어별 출력이 어떻게 생겼는지 나란히 볼 수 있습니다.

```
dotnet run --project src/SheetMan.csproj -- --recipe showcases/showcase.json
```

### 실제로 돌아가는 recipe들

[test/fixtures/recipes/](../test/fixtures/recipes/)에 회귀 스위트가 매번 실행하는 recipe가 서른 개 가까이 있습니다. 문서의 예제와 달리 **반드시 최신**입니다 — 낡으면 테스트가 깨지기 때문입니다.

|파일|무엇을 보여주는가|
|--|--|
|`core.json`|엑셀 하나에서 바이너리·JSON·C#·C++·HTML까지|
|`core-client.json` / `core-server.json`|`TargetSide`로 갈라 뽑기|
|`conformance.json`|13개 타깃 전부를 한 recipe에|
|`table-extension.json`|`.table`이 아닌 확장자로 맞추기|
|`databases.json`|MySQL / PostgreSQL / MongoDB / Redis|
|`history.json`|히스토리 기록|
|`core-dynamic.json`|`Targets` 목록만으로 전부 지정하기|

### 전체 예제 (모든 설정)

<details>
<summary>펼쳐보기</summary>

```json
{
  // 배열 셀의 구분자. 쉼표가 기본이 아닌 이유는 문장과 숫자 표기에 너무 흔하기 때문입니다.
  "ArrayDelimiter": ";",

  // 0번 라벨이 없는 enum에 `None = 0`을 넣어줍니다.
  // 켜두는 쪽이 기본인 이유: enum 타입의 필드는 값이 대입되기 전에도 뭔가를 들고 있어야 하는데,
  // 그게 이름 없는 0이면 디버거에서도 로그에서도 읽을 수 없기 때문입니다.
  // 시트에 적은 것만 정확히 나오길 원한다면 끄세요.
  "AutoInsertEnumNoneLabel": true,

  "Sources": {
    "Xlsx": [
      {
        "Path": "./sheets",
        "FileExtensionPatterns": ".xls;.xlsx",

        // 시트를 읽는 방식. 기본은 `sheetman` — 마커로 엔티티를 선언하는 우리 레이아웃입니다.
        // 다른 프로젝트의 기존 엑셀을 그대로 읽으려면 `rescue`. 자세한 건 sheets.md 참고.
        "Layout": "sheetman",

        // 읽을 시트 목록. 비우면 전부. 배열로도, `;`로 이은 문자열로도 쓸 수 있습니다.
        // `*` `?` 와일드카드가 파일 글롭과 같게 동작하고, 여기 적었는데 없는 시트는
        // 조용히 빠지는 대신 오류로 알려줍니다.
        "IncludeSheets": [],

        // 제외할 시트. IncludeSheets 다음에 적용됩니다.
        "ExcludeSheets": "*참고용*",

        // 인덱스 값이 겹칠 때: `error`(기본) / `keep-first` / `keep-last`.
        // 뒤의 둘은 `rescue` 레이아웃 전용이며, 버린 행을 전부 로그에 남깁니다.
        "OnDuplicateIndex": "error"
      }
    ],
    "GoogleSheets": [
      {
        // 이 파일은 커밋하지 마세요. .gitignore에 등록되어 있습니다.
        "ClientSecretFilename": "./googlesheets-client-secret.json",
        "SheetsId": "10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU"

        // Layout / IncludeSheets / ExcludeSheets / OnDuplicateIndex 는 여기서도 같습니다.
      }
    ]
  },

  "Exports": {
    "Binary": [
      {
        "Path": "./generated/binary",
        "FileExtension": ".table"
      }
    ],
    "Json": [
      {
        "Path": "./generated/json",
        // true면 이름 없이 값만 배열로 담습니다. 파일이 작아집니다.
        "UseCompactRowFormat": false,
        "Indented": false
      }
    ],

    // 데이터베이스 적재. 비밀값은 ${환경변수}로 빼세요.
    "MySql": [
      {
        "ConnectionString": "Server=db;Database=game;Uid=sheetman;Pwd=${DB_PASSWORD}",
        "NamePrefix": "sm_"
      }
    ],
    "PostgreSql": [
      {
        "ConnectionString": "Host=db;Database=game;Username=sheetman;Password=${DB_PASSWORD}",
        "Schema": "public",
        "NamePrefix": "sm_"
      }
    ],
    "MongoDb": [
      {
        // 데이터베이스 이름을 반드시 포함해야 합니다.
        "ConnectionString": "mongodb://db:27017/game",
        "NamePrefix": "sm_"
      }
    ],
    "Redis": [
      {
        "ConnectionString": "db:6379,password=${REDIS_PASSWORD}",
        "Database": 0,
        "NamePrefix": "sm_"
      }
    ]
  },

  "CodeGenerations": {
    "CSharp": [
      {
        // 출력 타겟 폴더입니다. 없으면 자동으로 만듭니다.
        "Path": "./generated/cs",
        "Namespace": "StaticData",
        "AccessorName": "SheetAccessor"
      }
    ],
    "Typescript": [
      {
        "Path": "./generated/ts",
        // true면 enum을 숫자 대신 문자열로 생성합니다.
        "UseStringEnum": false
      }
    ],
    "Cpp": [
      {
        "Path": "./generated/cpp",
        // `.`이나 `::`로 중첩 네임스페이스를 지정할 수 있습니다.
        "Namespace": "game::data",
        "AccessorName": "SheetAccessor"
      }
    ],
    "Html": [
      {
        "Path": "./generated/html"
      }
    ]
  }
}
```

</details>
