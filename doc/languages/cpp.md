# C++

> [언어별 가이드로](readme.md) · [문서 목록으로](../../readme.md)

---

## 무엇이 생성되는가

헤더 온리입니다. 소스 파일이 없습니다.

```
<Path>/
  <AccessorName>.h                  우산 헤더 — 이것만 include하면 전부 들어옵니다
  <AccessorName>_forward.h          레코드 전방선언 (테이블 간 참조용)
  <AccessorName>_<table>.h          테이블당 하나
  <AccessorName>_enum_<enum>.h      enum당 하나
  <AccessorName>_const_<set>.h      상수 세트당 하나
  sheetman/lite_binary_reader.h     바이너리 리더 (함께 생성됩니다)
```

## 필요한 것

|항목|값|
|--|--|
|C++|17 이상|
|외부 라이브러리|**없음**|
|include 경로|생성 폴더 하나. `lib/cpp`를 추가할 필요가 **없습니다** — 리더가 함께 생성됩니다|

## recipe 설정

```jsonc
"CodeGenerations": {
  "Cpp": [
    {
      "Path": "src/generated",
      "Namespace": "mygame::data",   // 비우면 전역 네임스페이스
      "AccessorName": "GameData",
      "BinaryTableFileExtension": ".table",
      "Sweep": true,
      "TargetSide": "s"
    }
  ]
}
```

## 쓰는 법

```cpp
#include "GameData.h"

mygame::data::Tables tables;
tables.read_all("./data");

const auto* sword = tables.item().find(1);
if (sword != nullptr) {
    // 참조는 로드 후 포인터로 연결됩니다.
    std::cout << sword->name << " / " << sword->category_id->name << "\n";
}

for (const auto& row : tables.item().records()) { /* ... */ }
```

확장자는 두 번째 인자입니다.

```cpp
tables.read_all("./data", ".bytes");
```

## 주의사항

**테이블 헤더는 서로를 include하지 않습니다.** 두 테이블이 서로를 참조하는 것은 시트에서 흔하고, 그러면 include 순환이 됩니다. 포인터 멤버는 불완전 타입만 있으면 되므로 모든 레코드는 `<AccessorName>_forward.h`에 전방선언되어 있고 테이블 헤더는 그것을 include합니다.

**enum은 다릅니다.** enum으로 선언된 필드는 포인터가 아니라 값이므로 완전 타입이 필요하고, 그 헤더는 실제 include입니다.

**헤더 하나하나가 단독으로 컴파일됩니다.** 우산 헤더를 거치지 않고 `<AccessorName>_item.h`만 include해도 됩니다 — 회귀 스위트가 모든 헤더를 번역 단위의 유일한 include로 컴파일해서 확인합니다.

**멤버 이름은 snake_case입니다.** C++ 키워드와 부딪히면 `sm_` 접두사가 붙습니다 (`class` → `sm_class`).

## 트러블슈팅

|증상|원인과 조치|
|--|--|
|`lite_binary_reader.h`를 찾을 수 없음|생성 폴더가 include 경로에 있는지 확인하세요. 리더는 그 아래 `sheetman/`에 함께 생성됩니다|
|`incomplete type` 오류|우산 헤더 대신 테이블 헤더만 include하고 다른 테이블의 레코드를 **역참조**했습니다. 전방선언은 포인터까지만 허용합니다 — 그 테이블의 헤더도 include하세요|
|참조가 `nullptr`|테이블 하나만 읽었거나, 시트가 그 셀에 `0`을 넣었습니다 (0은 "참조 없음")|
|`std::` 관련 링크 오류|헤더 온리라 링크할 것이 없습니다. 다른 문제입니다|
