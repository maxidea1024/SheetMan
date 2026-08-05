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

### 전체 예제

<details>
<summary>펼쳐보기</summary>

```json
{
  // 배열 셀의 구분자. 쉼표가 기본이 아닌 이유는 문장과 숫자 표기에 너무 흔하기 때문입니다.
  "ArrayDelimiter": ";",

  "Sources": {
    "Xlsx": [
      {
        "Path": "./sheets",
        "FileExtensionPatterns": ".xls;.xlsx"
      }
    ],
    "GoogleSheets": [
      {
        // 이 파일은 커밋하지 마세요. .gitignore에 등록되어 있습니다.
        "ClientSecretFilename": "./googlesheets-client-secret.json",
        "SheetsId": "10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU"
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
