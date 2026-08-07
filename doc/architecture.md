# 아키텍처와 개발

내부 구조, 패키징 시 주의점, 그리고 이 저장소에서 개발·테스트하는 법.

> [문서 목록으로](../readme.md)

---

## 아키텍처 메모

포맷을 **정의하는 코드는 하나**, 그것을 **구현하는 테이블 리더는 13개**입니다.

|위치|역할|
|--|--|
|`src/Exporters/LiteBinaryWriter.cs`|**포맷을 정의하는 writer.** 익스포터 내부에 있고 외부 의존이 없습니다.|
|`lib/<언어>/sheetman/...`|언어별 리더. `cs` `cpp` `c` `ts` `go` `rust` `python` `java` `kotlin` `ruby` `php` `dart` `unreal` 13개|

테이블 리더가 `lib/` 아래에 실제 파일로 존재하는 이유는 편집과 리뷰가 가능해야 하기 때문이고, 임베디드 리소스로 읽어 쓰는 이유는 배포본과 커밋된 소스가 어긋날 수 없게 하기 위함입니다.

13개 테이블 리더가 하나의 정의를 각자 구현하므로 어긋날 수 있고, 어긋나면 **실패하는 게 아니라 값이 달라집니다**. 그래서 적합성 코퍼스가 있습니다 — 경계값 한 테이블을 전 언어로 읽어 익스포터 JSON과 대조합니다. 실제로 이 방식이 Go의 틱 오버플로, Java의 부호 없는 시프트, Dart의 웹 32비트 비트연산, 여러 언어의 비ASCII 인코딩 문제를 출시 전에 잡았습니다.

**Unreal 리더가 C++ 리더의 사본이 아닌 이유.** 처음에는 공유했습니다 — 포맷이 사는 곳이고 이미 코퍼스가 검증하니까요. 그 대가는 엔진에 이미 있는 것을 다시 만드는 것이었습니다. `std::string`으로 받아 `FString`으로 옮기느라 문자열 셀마다 할당 두 번, `Uuid`를 36자 텍스트로 만들어 `FGuid::Parse`에 넘기느라 uuid마다 할당 세 번과 파싱 한 번. 더 나쁜 건 그 테이블 리더가 실패를 **예외로** 알린다는 점이었습니다. Unreal은 모듈을 예외 비활성으로 빌드하므로, 손상된 `.scb` 하나가 `bool`을 반환하겠다고 선언한 함수 안에서 프로세스를 끝냈습니다.

그래서 `lib/unreal`은 `lib/cpp`의 래퍼가 아니라 형제입니다. `FString` `TArray` `FGuid` `FDateTime` `FTimespan` `int32`를 직접 채우고, 실패는 **누적되는 플래그**로 알립니다 — 20개 필드를 연달아 읽고 테이블 끝에서 한 번만 물어보면 되도록. 와이어 포맷은 그대로라 코퍼스가 계속 적용됩니다.

**코드 생성기와 타깃 추가.** 타깃은 `[SheetManTarget]` 어트리뷰트로 등록되고 실행 어셈블리 스캔으로 발견됩니다. 언어 하나를 추가하는 비용은 테이블 리더 · 템플릿(`src/templates/*.sbn`) · 뷰 · 제너레이터 · 50줄짜리 적합성 하네스이며, `RecipeModel`이나 `Program`은 건드리지 않습니다.

예전에는 writer와 C# 리더가 Unity 플러그인으로 설치해야 하는 하나의 공유 런타임(3,600줄)이었습니다. 생성 코드가 쓰는 건 그중 네 개 멤버뿐이었고, 그 결합 때문에 변환기 자체가 Unity가 받아들이는 C# 수준에 묶여 있었습니다. 더 나쁜 건 writer와 테이블 리더가 한 몸이어서 **와이어 포맷 오류가 드러나지 않았다는 점**입니다 — C# 안에서 왕복하면 무엇을 잘못 쓰든 제대로 읽혔습니다.

`test/EmittedCodeLanguageCheck`는 아무것도 배포하지 않는 프로젝트입니다. C# 리더를 `netstandard2.1`로 컴파일해 Unity 2020.3이 받아들이는 C# 8을 넘지 않도록 컴파일러가 강제하게 하는 용도입니다.

**생성된 C#은 소비자가 정의할 심볼이 없습니다.** 유니티가 스스로 정의하는 심볼(`UNITY_5_3_OR_NEWER`, `UNITY_2021_2_OR_NEWER`, `UNITY_WEBGL`)로 갈리고, 외부 패키지 의존도 없습니다.

|컴파일 대상|읽기 방식|이유|
|--|--|--|
|경로에 `://` 가 있으면 (유니티 전용)|`UnityWebRequest`|**File API로 읽을 수 없는 두 경우**를 한 조건이 덮습니다 — 아래 참조|
|일반 .NET, 유니티 2021.2 이상|`File.ReadAllBytesAsync`|2021.2에서 API 레벨이 .NET Standard 2.1로 올라가며 생긴 것. 진짜 비동기 I/O로 스레드를 잡지 않습니다|
|유니티 2019 · 2020|`Task.Run(File.ReadAllBytes)`|.NET Standard 2.0에는 비동기 파일 API가 없어 워커 스레드가 유일한 방법|
|유니티 WebGL (그 외)|동기 읽기|WebGL은 스레드가 없어 `Task.Run`이 **인라인 실행**됩니다 — 예전 코드는 프리즈를 막으려던 자리에서 프리즈를 만들었습니다. 남는 건 IndexedDB 위 동기식 FS인 persistentDataPath뿐입니다|

읽기 방식을 바꾸려면 `Tables.ReadAllBytesAsync`에 직접 대입하면 됩니다 — 팩 파일, CDN, Addressables 모두 그 자리에서 갈아끼웁니다.

유니티 분기 두 개는 **실제로 컴파일해서 검증**합니다(`CsGeneratorTests`). 그 전에는 셋 중 하나도 컴파일된 적이 없어, 어느 것이든 오래 깨져 있을 수 있었습니다. `UnityEngine.Networking`을 참조하는 분기는 엔진이 필요해 게이트 밖입니다.

## 패키징 — 배포 후에도 데이터에 닿는가

엔진 애셋 포맷(`.uasset` / `.asset`)으로 바꿀 필요는 없습니다. **두 엔진 모두 원본 파일을 그대로 배포하고 읽게 해줍니다.** 다만 각자 조건이 하나씩 있고, 둘 다 "에디터에서는 되는데 패키징하면 안 되는" 형태로 나타납니다 — 조건과 대처는 각 언어 문서에 있습니다.

- 유니티: [C# / Unity](languages/csharp.md#주의사항)
- 언리얼: [Unreal Engine](languages/unreal.md#패키징--데이터가-빌드에-들어가는가)

성능 면에서 writer와 C# 리더는 모두 `Span` 기반이고 값마다 임시 할당을 하지 않습니다. 문자열은 버퍼로 직접 인코딩되고(중간 배열 없음), uuid는 제자리에 기록되며, 테이블 바이트는 파일 쓰기로 복사 없이 넘어갑니다. 테이블 리더 쪽도 레코드가 실제로 보유하는 문자열·배열 외에는 할당이 없습니다.

## 개발 / 테스트

```
dotnet test            # 전체 회귀 스위트
```

스위트는 실제 산출물을 만들어 검증합니다.

|검증|방식|
|--|--|
|골든 비교|`test/fixtures/xlsx/`의 워크북을 변환하고 모든 산출물을 `test/fixtures/golden/`과 비교합니다. 타임스탬프만 정규화합니다.|
|TypeScript|생성된 코드를 실제 `tsc`로 타입 체크합니다.|
|C++|생성된 헤더를 컴파일하고, 익스포터가 쓴 `.scb`를 읽어 JSON 익스포터 결과와 대조합니다.|
|C#|생성된 접근자를 **아무것도 설치하지 않은 상태로** 컴파일하고, 익스포터가 쓴 `.scb`를 읽어 대조합니다.|
|TypeScript 왕복|같은 테이블을 JSON과 바이너리에서 각각 읽어 필드 단위로 비교합니다. 두 경로가 어긋나면 실패합니다.|
|방출 코드 언어 수준|C# 리더를 `netstandard2.1`로 컴파일해 Unity 2020.3이 받아들이는 C# 8을 넘지 않는지 확인합니다.|
|데이터베이스|`docker compose`로 MySQL / PostgreSQL / MongoDB / Redis를 띄우고 실제로 적재한 뒤 서버에 직접 질의합니다.|
|적합성 코퍼스|경계값 테이블 하나를 **12개 언어로 각각 컴파일·실행해서 읽고** 익스포터 JSON과 대조합니다.|
|C 헤더의 C++ 호환|생성된 C 헤더를 **C++로도 컴파일**합니다. `extern "C"`로 약속해놓고 `class`·`delete` 멤버 때문에 못 쓰는 일이 없도록.|
|Unreal|생성된 헤더를 **실제 UnrealHeaderTool**에 통과시킵니다 (`SHEETMAN_UE_ROOT`를 엔진 루트로 지정할 때). 엔진 없이도 코퍼스를 **읽어서** 익스포터와 대조하고 — 엔진 타입 스텁으로 빌드합니다 — 모듈에 `std::`·표준 헤더·`throw`가 없는지, 손상된 테이블을 거부하는지 확인합니다.|
|히스토리|실제 MySQL에 스냅샷을 기록하고 읽어옵니다. 같은 커밋 재기록, 삭제된 행 정리, 브랜치 분리, 정리(prune)까지 서버에 직접 질의해 확인합니다.|
|웹서버|실제 포트에 서버를 띄우고, **API 응답과 CLI 출력을 바이트 단위로 비교**합니다. 토큰 없는 외부 바인딩 거부도 확인합니다.|
|데이터 갱신기|13개 언어의 업데이터를 각자의 툴체인으로 빌드해 **실제 HTTP 서버**에 붙입니다 — 받은 바이트 비교, 해시 불일치 거부와 캐시 불변, 5xx 재시도, 404 비재시도. 직접 쓴 MD5는 공개 벡터로 따로 확인합니다.|
|셀프컨테인드 배포|CI가 매 실행마다 linux-x64로 퍼블리시하고 그 산출물로 변환을 돌립니다.|

생성기나 템플릿을 건드렸다면 **세 가지를 순서대로** 합니다.

```
set SHEETMAN_UE_ROOT=C:/path/to/UnrealEngine     # 언리얼 게이트를 돌릴 때만

set SHEETMAN_UPDATE_GOLDEN=1 && dotnet test      # 1. 골든 다시 기록
dotnet run --project src/SheetMan.csproj -- --recipe showcases/showcase.json
                                                 # 2. 쇼케이스 다시 생성 (커밋 대상입니다)
dotnet test                                      # 3. 기록 없이 검증
```

`SHEETMAN_UPDATE_GOLDEN=1` 실행에서는 `core-dynamic`이 실패합니다 — `core`의 골든을 공유하는 시나리오라 스스로 기록할 수 없다고 거부하는 것이고, 3단계에서 통과하면 정상입니다.

의도한 출력 변경이 있을 때는 골든을 갱신하고 git diff로 리뷰합니다.

```
SHEETMAN_UPDATE_GOLDEN=1 dotnet test
```

**전 언어 산출물 비교.** [showcases/](../showcases/)에 `reserved-words` 픽스처를 **13개 생성기 전부로** 만든 결과가 커밋되어 있습니다. 게이트가 아니라 읽기 위한 것입니다 — 스위트는 전부 컴파일해 주지만, 11개가 무엇을 뱉는지 나란히 놓고 보여주지는 못합니다. 그 차이가 실제로 문제를 잡습니다: Unreal 타깃이 일반 C++ 리더를 싣고 있던 것도, Dart가 `int int = 0;`을 내던 것도 여기서 드러났습니다.

제너레이터·템플릿·테이블 리더를 건드렸다면 다시 만들고 diff를 리뷰하세요.

```
dotnet run --project src/SheetMan.csproj -- --recipe showcases/showcase.json --silent
```

픽스처 `.xlsx`는 [test/fixtures/tools/FixtureGen](../test/fixtures/tools/FixtureGen)이 생성합니다. 불투명한 바이너리가 아니라 코드로 리뷰할 수 있게 하기 위함입니다. 생성기를 수정했다면 다시 돌려서 커밋하세요.

```
dotnet run --project test/fixtures/tools/FixtureGen
```

테스트 컨테이너는 실행 후에도 남습니다(4개 엔진을 매번 내리고 올리는 비용이 테스트 자체보다 큽니다). 정리는 아래와 같이 합니다.

```
cd test/fixtures/databases && docker compose down -v
```

언어별 검증과 데이터베이스 검증은 툴체인이 없으면 **건너뛰지 않고 실패**합니다. 조용히 꺼지는 게이트는 없는 게이트보다 나쁘기 때문입니다. 로컬에서 전부 돌리려면 g++/gcc 또는 MSVC(C와 C++ 양쪽), Node, Go, Rust, Python, JDK, Kotlin, Ruby, PHP, Dart, 그리고 Docker가 필요합니다. C·C++ 업데이터 게이트는 **libcurl**도 봅니다 — 리눅스에선 `libcurl4-openssl-dev`, 윈도우에선 `vcpkg install curl:x64-windows`이고, 그 외의 곳에 있으면 `SHEETMAN_LIBCURL_ROOT`로 알려줍니다. CI가 그 전부를 설치하므로, 로컬에서는 건드린 부분만 골라 돌리고 나머지는 CI에 맡겨도 됩니다.

```
dotnet test --filter "FullyQualifiedName~Conformance"    # 언어별 리더
dotnet test --filter "FullyQualifiedName~History"        # 히스토리와 웹서버
```
