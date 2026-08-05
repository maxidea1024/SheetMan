# Kotlin

> [언어별 가이드로](readme.md) · [문서 목록으로](../../readme.md)

---

## 무엇이 생성되는가

```
<Path>/<PackageName as folders>/
  <AccessorName>.kt          접근자 (object)
  tables/<Table>Table.kt     테이블당 하나
  enums/<Enum>.kt            enum당 하나
  constants/<Set>.kt         상수 세트당 하나
<Path>/sheetman/
  LiteBinaryReader.kt        바이너리 리더 (함께 생성됩니다)
```

## 필요한 것

|항목|값|
|--|--|
|Kotlin|2.1로 검증|
|외부 라이브러리|**없음**|

## recipe 설정

```jsonc
"Targets": [
  {
    "Type": "kotlin",
    "Path": "src/main/kotlin",
    "PackageName": "com.mygame.data",
    "AccessorName": "GameData",
    "BinaryTableFileExtension": ".table",
    "Sweep": true,
    "TargetSide": "s"
  }
]
```

## 쓰는 법

**접근자는 `object`입니다.** 인스턴스를 만들지 않습니다.

```kotlin
import com.mygame.data.GameData

GameData.readAll("./data")

val sword = GameData.item.find(1)
if (sword != null) {
    // 참조는 로드 후 실제 레코드로 연결됩니다.
    println("${sword.name} / ${sword.categoryId?.name}")
}

for (row in GameData.item.records) { /* ... */ }
```

확장자는 기본 인자입니다.

```kotlin
GameData.readAll("./data", ".bytes")
```

## 주의사항

**참조는 nullable입니다.** 시트가 `0`을 넣으면 "참조 없음"이고, 그때 값은 `null`입니다.

**미사용 import는 경고입니다.** Kotlin은 오류가 아니라 경고라, 생성물이 파일마다 같은 목록을 가져도 빌드가 깨지지 않습니다. Go가 같은 자리에서 오류를 내는 것과 대비됩니다.

**키워드는 백틱으로 이스케이프합니다.** 이름을 바꾸지 않고 `` `class` ``로 감싸므로, 생성된 멤버 이름이 시트의 이름과 그대로 같습니다.

**`datetime`과 `timespan`은 `Long`입니다.** .NET 틱이 그대로 들어옵니다.

## 트러블슈팅

|증상|원인과 조치|
|--|--|
|`Unresolved reference: GameData`|`PackageName`과 import가 맞는지, `Path`가 소스 루트인지 확인하세요|
|`Expression 'item' of type 'ItemTable' cannot be invoked`|`object`라서 `GameData.item`이지 `GameData().item`이 아닙니다|
|참조가 `null`|`readAll` 대신 테이블 하나만 읽었거나, 시트가 그 셀에 `0`을 넣었습니다|
