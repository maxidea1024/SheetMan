# C

> [언어별 가이드로](readme.md) · [문서 목록으로](../../readme.md)

---

## 무엇이 생성되는가

```
<Path>/
  <AccessorName>.h                       우산 헤더 — 이것만 include하면 전부 들어옵니다
  <AccessorName>.c                       접근자 구현 (LoadAll, Free, 참조 연결)
  <AccessorName>_Forward.h               레코드 전방선언 (테이블 간 참조용)
  <AccessorName>_Reader.c                리더 구현을 담는 번역 단위 하나
  <AccessorName>_<Table>.h / .c          테이블당 하나씩
  <AccessorName>_Enum<Enum>.h            enum당 하나
  <AccessorName>_Const<Set>.h / .c       상수 세트당. `.c`는 헤더가 담을 수 없는 값이 있을 때만
  sheetman/sheetman_lite_binary_reader.h 바이너리 리더 (함께 생성됩니다)
```

## 필요한 것

|항목|값|
|--|--|
|C|C99 이상|
|외부 라이브러리|**없음.** 표준 라이브러리만|
|빌드|생성된 `.c`를 **전부** 빌드에 넣으세요|

헤더는 `extern "C"`로 감싸여 있어 C++에서도 include할 수 있습니다. 회귀 스위트가 C++ 컴파일러로도 컴파일해서 확인합니다.

## recipe 설정

```jsonc
"Targets": [
  {
    "Type": "c",
    "Path": "src/generated",
    "AccessorName": "GameData",     // 모든 타입·함수 이름의 접두사가 됩니다
    "BinaryTableFileExtension": ".table",
    "Sweep": true,
    "TargetSide": "s"
  }
]
```

C에는 네임스페이스가 없으므로 `AccessorName`이 충돌 회피의 전부입니다. 타입은 `GameData_ItemRecord_t`, 함수는 `GameData_ItemLoad`처럼 나옵니다.

## 쓰는 법

```c
#include "GameData.h"

GameData_t data;
char error[512];

if (!GameData_LoadAll(&data, "./data", error, sizeof error)) {
    fprintf(stderr, "load failed: %s\n", error);
    return 1;
}

const GameData_ItemRecord_t* sword = GameData_ItemFind(&data.item, 1);
if (sword != NULL) {
    /* 참조는 로드 후 포인터로 연결됩니다. */
    printf("%s / %s\n", sword->name, sword->category_id->name);
}

int32_t row;
for (row = 0; row < data.item.count; ++row) {
    const GameData_ItemRecord_t* r = &data.item.records[row];
    /* ... */
}

GameData_Free(&data);
```

확장자가 다르면 짝이 되는 함수를 씁니다. C에는 기본 인자가 없습니다.

```c
GameData_LoadAllWithExtension(&data, "./data", ".bytes", error, sizeof error);
```

## 주의사항

**메모리는 테이블이 소유합니다.** 테이블마다 아레나가 하나이고, 레코드의 문자열과 배열은 전부 그 안을 가리킵니다. `GameData_Free` 한 번으로 전부 해제되고, 어떤 레코드의 포인터도 그보다 오래 살지 않습니다. 개별 `free`를 부르지 마세요.

**던지지 않습니다.** 실패는 `false` 반환과 `error` 버퍼입니다. 실패한 로드는 자기가 잡았던 것을 해제하고 테이블을 비워두므로, 반환값을 무시해도 절반만 든 데이터가 아니라 빈 테이블을 보게 됩니다.

**`_Reader.c`를 빼지 마세요.** 리더는 헤더 하나에 선언과 구현이 함께 있고, 구현은 정확히 한 번역 단위에서만 켜져야 합니다. 그 일만 하는 파일이 `<AccessorName>_Reader.c`입니다.

**테이블 헤더는 서로를 include하지 않습니다.** 두 테이블이 서로를 참조하면 순환이 되기 때문입니다. 포인터 멤버에는 불완전 타입이면 충분하므로 모든 레코드가 `_Forward.h`에 한 번 전방선언되어 있습니다. C99에서 같은 `typedef`를 두 번 적는 것은 제약 위반이라, 각 헤더가 따로 적지 않고 한 곳에 모았습니다.

**이름 규칙.** 타입은 `Prefix_NameRecord_t`, 함수는 `Prefix_NameVerb`, 멤버는 snake_case, 상수는 SCREAMING_SNAKE입니다. Doom·Quake 계열의 관례에 네임스페이스 대용의 접두사를 붙인 형태입니다.

## 트러블슈팅

|증상|원인과 조치|
|--|--|
|`sm_read_int32` 등이 정의되지 않음 (링크 오류)|`<AccessorName>_Reader.c`를 빌드에 넣지 않았습니다|
|`sm_read_*`가 두 번 정의됨|`SHEETMAN_LITE_BINARY_IMPLEMENTATION`을 다른 곳에서 또 정의했습니다. 그 일은 `_Reader.c`만 합니다|
|`incomplete type` 오류|테이블 헤더만 include하고 다른 테이블의 레코드를 역참조했습니다. 그 테이블의 헤더도 include하세요|
|C++에서 컴파일 오류|헤더는 C++로도 컴파일되도록 검사됩니다. 재현되면 버그입니다|
|해제 후 문자열이 깨짐|레코드의 포인터는 테이블 아레나를 가리킵니다. `Free` 뒤에는 유효하지 않습니다|
