# 다른 규칙으로 쓰인 시트 읽기 — `rescue` 적용 기록

> [문서 목록으로](../readme.md)

---

다른 규칙으로 작성된 게임 데이터 시트 한 벌을 `rescue` 레이아웃으로 읽은 기록입니다. 무엇을 설정했고 무엇이 걸렸는지를 순서대로 적습니다.

`rescue` 레이아웃의 규칙 자체는 [시트 작성](sheets.md#rescue-사례)에 있습니다.

## 시트의 모양

구글 스프레드시트로 작성되어 있고, 유니티 에디터 툴이 CSV로 내려받아 C# 클래스를 만들어 쓰는 구조였습니다.

```
row 1   필드 설명       ← `#`으로 시작하면 그 컬럼은 빠짐
row 2   필드 이름       ← 첫 컬럼이 인덱스
row 3   필드 타입       ← Int / int / IntArray / enum:GradeType ...
row 4~  데이터
```

마커가 없고 **시트 탭 이름이 곧 테이블 이름**입니다. enum은 `TableEnums` 시트 하나에 컬럼 단위로 모여 있고, 값 없이 라벨 이름만 적혀 있습니다.

`sheetman` 기본 레이아웃과 겹치는 부분이 거의 없어, 시트를 고치는 대신 읽는 쪽을 하나 더 만들었습니다.

## 1. 읽을 시트를 정한다

워크북 17개에 시트가 85장 있고, 그중 게임이 실제로 쓰는 것은 일부입니다. 나머지는 참고용 탭, 작업 메모, 만들다 만 표입니다. 어느 쪽인지는 시트만 봐서는 알 수 없고, **클라이언트가 다운로드 대상으로 등록해둔 목록**에 68장이 적혀 있었습니다.

그 목록을 `IncludeSheets`에 옮겼습니다.

```jsonc
{
  "Sources": {
    "Xlsx": [{
      "Path": "project-rescue-xlsxs",
      "FileExtensionPatterns": ".xlsx",
      "Layout": "rescue",

      "IncludeSheets": [
        "TableEnums",          // enum 모음. 모든 enum: 컬럼이 여기를 봅니다

        "CurrencyTable", "MaterialTable", "PackageTable", "EquipTable",
        "CharacterTable", "SkillTable", "ArtifactTable", "StageTable",
        "SDAgencyTable", "GoldDungeonStageTable", "StatGrowthTable"
        // ... 전체 68개
      ]
    }]
  }
}
```

적어놓고 없는 시트는 오류입니다 — 시트 이름이 바뀌면 조용히 빠지지 않고 변환이 멈추며, 실제로 있는 시트 목록을 함께 보여줍니다. 전체는 [project-rescue-xlsxs/recipe.jsonc](../project-rescue-xlsxs/recipe.jsonc)에 있습니다.

> 범위를 좁힌 효과가 큽니다. 85장 전부를 대상으로 했을 때는 관용 규칙이 넷 필요했는데, 68장으로 좁히자 셋이 필요 없어졌습니다. 미정의 enum, `none`이 든 숫자 컬럼, 정의에 없는 라벨이 전부 등록되지 않은 시트에만 있었습니다.

## 2. 걸린 것

68장에서 넷이 걸렸습니다.

|무엇|어디|처리|
|--|--|--|
|Id가 56행 중복|`ArtifactLevelTable`|원본의 값 문제. `OnDuplicateIndex: keep-first`로 우회하고 원본에 보고|
|Id 칸이 빈 행 9개|`SkillTable`|작성 중인 행. 규칙으로 건너뛰고 경고|
|Id가 `#9`, `#10`인 행 2개|`CollectionGroupTable`|행 주석. 규칙으로 건너뜀|
|첫 컬럼이 `string`|`ConfigTable`|순번 `Index` 컬럼을 앞에 만들고 원래 `Id`는 보조 인덱스로|

넷 중 셋은 `rescue` 레이아웃의 기본 규칙으로 처리됩니다. 넷째도 마찬가지이고, 결과적으로 `FindByIndex(int)`와 `FindById(string)`이 둘 다 나옵니다.

recipe에 추가한 설정은 한 줄뿐이었습니다.

```jsonc
// ArtifactLevelTable 48~104행의 Id가 전부 45입니다. 원본 스프레드시트의
// 값 문제이고, 어느 쪽으로 처리해도 레벨 46~101에는 도달할 수 없습니다.
// 고쳐질 때까지 나머지를 변환하기 위한 설정이며, 버린 행은 전부 로그에
// 남습니다. 원본이 고쳐지면 `error`로 되돌리세요.
"OnDuplicateIndex": "keep-first"
```

> 관용 설정에는 왜 켰는지와 언제 끌지를 같이 적어두는 편이 낫습니다. 그래야 나중에 지울 수 있습니다.

## 3. 변환과 확인

```
sheetman --recipe project-rescue-xlsxs/recipe.jsonc
```

68장에서 **테이블 67개, enum 37개, 103,395행**이 나왔습니다. 산출물은 [project-rescue-xlsxs/out/](../project-rescue-xlsxs/out/)에 있고, 13개 언어 코드와 바이너리·JSON·HTML 문서가 들어 있습니다.

확인한 방법은 셋입니다.

- **HTML 문서.** 실제로 로드된 값이 표로 나오므로 값이 이상한 컬럼이 눈에 띕니다.
- **생성 코드 컴파일.** C# 107개 파일이 경고 없이 컴파일됐습니다.
- **바이너리 되읽기.** 값이 왕복하는지 확인했습니다.

## 4. 원본에 돌려보낼 것

변환기가 잡아낸 것 중 데이터 자체의 문제는 별도 보고서로 정리했습니다. 등록 목록과 대조하면 **지금 쓰이고 있어 문제가 되는 것**과 **아직 안 쓰이는 시트에 있는 것**이 구분되므로, 그 구분을 붙여서 전달했습니다.

## 나오지 않는 것

`rescue`로 읽은 결과는 `sheetman` 레이아웃으로 읽은 것과 동일한 자격을 가집니다 — 같은 코드, 같은 바이너리, 같은 문서, 같은 검증.

다만 `foreign` 참조, `TargetSide`, `@N` 와이어 태그, `*` 보조 인덱스, 연속 번호 컬럼 묶기는 **SheetMan이 자체 레이아웃에 정의한 표기**이고 시트에 적을 자리가 없으므로 나오지 않습니다. 자세한 것은 [시트 작성 — 두 레이아웃의 차이](sheets.md#두-레이아웃의-차이)에 있습니다.

enum 값은 라벨이 등장한 순서로 매겨집니다. `rescue` 시트에 값을 적는 칸이 없기 때문입니다. 내부적으로는 일관되지만, **라벨을 지우거나 순서를 바꾸면 이미 내보낸 데이터의 의미가 바뀝니다.** 라벨은 뒤에 붙이는 편이 안전합니다.

## 두 레이아웃을 같이 쓰기

레이아웃은 소스 항목마다 지정하므로 한 recipe에서 같이 읽을 수 있습니다. enum 선언은 레이아웃을 가로질러 해석되므로, 한쪽에서 선언한 enum을 다른 쪽 테이블이 타입으로 써도 됩니다.

배열 구분자가 서로 다르면 소스 항목에 `ArrayDelimiter`를 적습니다. recipe 전체 설정보다 항목 쪽이 우선합니다.

```jsonc
"Xlsx": [
  { "Path": "./sheets",       "Layout": "sheetman" },
  { "Path": "./other-sheets", "Layout": "rescue", "ArrayDelimiter": "|" }
]
```

이름 정규화는 양쪽이 같습니다. `STAR_LEVEL`은 `STARLEVEL`이 되고, `Icon_Path`와 `IconPath`는 같은 이름으로 충돌해 오류가 납니다. 그대로 두면 snake_case 언어에서 13번 서로 다르게 깨지므로 변환 단계에서 잡습니다. 데이터 셀은 원문(`STAR_LEVEL`)으로 매칭되므로 시트는 고치지 않아도 됩니다.
