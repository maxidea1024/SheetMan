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

## 시간 타입은 `std::chrono`입니다

시트의 `datetime`과 `timespan`은 표준 시간 타입으로 나옵니다. 틱 정수를 들고 다니며 직접 환산할 일이 없습니다.

|시트 타입|C++ 타입|
|--|--|
|`timespan`|`sheetman::TimeSpan` = `std::chrono::duration<int64_t, std::ratio<1, 10'000'000>>`|
|`datetime`|`sheetman::DateTime` = `std::chrono::time_point<std::chrono::system_clock, sheetman::TimeSpan>`|

```cpp
const auto* item = data.item().find(1);

// 원하는 단위로 변환은 chrono가 합니다. 손실이 생기는 변환은 컴파일러가 막습니다.
auto seconds = std::chrono::duration_cast<std::chrono::seconds>(item->cooldown);

// 표준 라이브러리와 바로 이어집니다.
std::time_t when = std::chrono::system_clock::to_time_t(
    std::chrono::time_point_cast<std::chrono::system_clock::duration>(item->released_at));
```

**기간 단위가 100나노초(.NET 틱)인 이유**는 그것이 파일에 실린 단위라 아무것도 잃지 않기 때문입니다. `std::chrono::nanoseconds`로 두면 `TimeSpan`의 최대값(9.2e18틱)이 64비트를 넘칩니다.

**에폭은 유닉스 에폭입니다.** 파일은 .NET 기준(0001-01-01)으로 실려 오고, 리더가 읽는 순간 한 번 옮깁니다 — C++의 모든 시계와 C 라이브러리가 합의한 기준이 그쪽이기 때문입니다. .NET 쪽과 틱으로 이야기해야 한다면 `sheetman::to_net_ticks(value)`와 `sheetman::from_net_ticks(ticks)`가 있습니다.

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
