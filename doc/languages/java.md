# Java

> [언어별 가이드로](readme.md) · [문서 목록으로](../../readme.md)

---

## 무엇이 생성되는가

```
<Path>/<PackageName as folders>/
  <AccessorName>.java     접근자
  <Table>Record.java      테이블당 둘 — 레코드와
  <Table>Table.java                    테이블
  <Enum>.java             enum당 하나
  <Set>.java              상수 세트당 하나
<Path>/sheetman/
  LiteBinaryReader.java   바이너리 리더 (함께 생성됩니다)
```

Java는 public 타입이 자기 이름과 같은 파일에 혼자 있어야 하므로 **테이블당 파일이 둘**입니다. 레코드를 테이블 안에 중첩해 `ItemTable.Record`로 부르는 대안도 있었지만, 이름이 나빠지는 값으로 파일 하나를 아끼는 것은 남는 장사가 아닙니다.

## 필요한 것

|항목|값|
|--|--|
|Java|21로 검증. 리더에 특별한 문법은 없지만 그 아래는 확인하지 않았습니다|
|외부 라이브러리|**없음**|

## recipe 설정

```jsonc
"Targets": [
  {
    "Type": "java",
    "Path": "src/main/java",
    "PackageName": "com.mygame.data",
    "AccessorName": "GameData",
    "BinaryTableFileExtension": ".table",
    "Sweep": true,
    "TargetSide": "s"
  }
]
```

`Path`는 소스 루트입니다. 패키지 이름이 그 아래 폴더로 펼쳐집니다.

## 쓰는 법

```java
import com.mygame.data.GameData;
import com.mygame.data.ItemRecord;

GameData data = new GameData();
data.readAll("./data");

ItemRecord sword = data.item.find(1);
if (sword != null) {
    // 참조는 로드 후 실제 레코드로 연결됩니다.
    System.out.println(sword.name + " / " + sword.categoryId.name);
}

for (ItemRecord row : data.item.records()) { /* ... */ }
```

Java에는 기본 인자가 없으므로 확장자는 오버로드입니다.

```java
data.readAll("./data", ".bytes");
```

## 주의사항

**전부 한 패키지에 평평하게 놓입니다.** 그래서 생성된 타입끼리 import가 하나도 없습니다. `tables`·`enums` 하위 패키지로 나누면 서로를 import해야 합니다.

**`datetime`과 `timespan`은 `long`입니다.** .NET 틱이 그대로 들어옵니다. `Instant`나 `Duration`으로 바꾸고 싶으면 직접 변환하세요.

**`uuid`는 `LiteBinaryReader.Uuid`입니다.** `java.util.UUID`가 아닙니다 — 바이트 순서가 .NET의 것이라 그대로 담습니다.

**멤버 이름은 camelCase입니다.** Java 키워드는 전부 소문자라 대부분 부딪히지 않지만, 부딪히는 경우는 이스케이프됩니다.

## 트러블슈팅

|증상|원인과 조치|
|--|--|
|`class X is public, should be declared in a file named X.java`|생성물에서 나면 버그입니다. 손으로 파일을 옮겼는지 확인하세요|
|`package com.mygame.data does not exist`|`Path`가 소스 루트인지 확인하세요. 패키지 폴더는 그 아래에 생성됩니다|
|참조가 `null`|`readAll` 대신 테이블 하나만 읽었거나, 시트가 그 셀에 `0`을 넣었습니다|
|`Uuid`를 `java.util.UUID`에 대입할 수 없음|다른 타입입니다. 바이트로 꺼내 변환하세요|
