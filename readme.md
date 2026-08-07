# SheetMan

### 기획자는 시트에 적고, 프로그래머는 타입으로 읽습니다

게임이나 앱의 밸런스·설정 데이터는 대개 스프레드시트에 있습니다. 그걸 프로그램에서 쓰려면 누군가 읽는 코드를 쓰고, 컬럼이 바뀔 때마다 손보고, 오타는 실행해봐야 압니다.

SheetMan이 그 사이를 맡습니다. 시트에서 **읽는 코드와 데이터 파일**을 만들어내고, 잘못 적은 값은 게임에 실리기 전에 **변환 단계에서** 잡습니다.

![임포트 → 검증 → 내보내기/코드생성 파이프라인](doc/pipeline.svg)

## 시트에 무엇을 적을 수 있나

세 가지입니다. 시트의 셀에 마커를 적으면 그 자리가 엔티티가 됩니다.

|엔티티|마커|무엇인가|나오는 것|
|--|--|--|--|
|**테이블**|`~~table:Item~~`|행과 열로 된 데이터. 기본 인덱스와 보조 인덱스, 다른 테이블 참조, 배열 컬럼|레코드 타입, 인덱스별 조회, 데이터 파일|
|**enum**|`~~enum:Grade~~`|이름 붙은 정수 값의 집합. 테이블 컬럼의 타입으로 씁니다|언어별 열거형 타입|
|**상수셋**|`~~const:Limits~~`|이름·타입·값의 목록. 행이 아니라 개별 상수입니다|언어별 상수 선언|

한 시트에 여러 개를 놓아도 되고, 어디에 놓아도 됩니다. 자세한 것은 [시트 작성](doc/sheets.md)에 있습니다.

### 다른 규칙으로 쓰인 시트 읽기

위 마커 방식이 기본(`sheetman` 레이아웃)이지만, **다른 규칙으로 작성된 시트도 그대로 읽을 수 있습니다.** 시트를 먼저 고치지 않아도 됩니다.

```jsonc
"Xlsx": [
  { "Path": "./sheets",       "Layout": "sheetman" },
  { "Path": "./other-sheets", "Layout": "rescue"   }
]
```

레이아웃은 **소스 항목마다** 지정하므로 한 recipe에서 섞어 읽을 수 있고, 한쪽에서 선언한 enum을 다른 쪽 테이블이 타입으로 써도 됩니다.

- 레이아웃별 시트 규칙: [시트 작성 — `rescue` 사례](doc/sheets.md#rescue-사례)
- recipe 설정: [Recipe 파일 — Layout](doc/recipe.md#layout--시트를-읽는-방식)
- **실제 적용 기록**: [다른 규칙으로 쓰인 시트 읽기](doc/adopting-a-project.md)

---

## 문서

|문서|내용|
|--|--|
|[시트 작성](doc/sheets.md)|엑셀·구글 스프레드시트에 데이터를 배치하는 법, 엔티티 마커, 이름 규칙, 지원 타입, 서버/클라 분리, 정적 검증, **시트 레이아웃**|
|[**다른 규칙으로 쓰인 시트 읽기**](doc/adopting-a-project.md)|`rescue` 레이아웃을 실제 프로젝트에 적용한 기록|
|[CLI](doc/cli.md)|빌드하고 실행하는 법, 명령줄 옵션|
|[Recipe 파일](doc/recipe.md)|무엇을 어디서 읽어 어디로 내보낼지 적는 파일|
|[내보내기](doc/exports.md)|바이너리·JSON 파일과 MySQL / PostgreSQL / MongoDB / Redis 적재. **바이너리를 쓰는 이유**|
|[바이너리 형식](doc/binary-format.md)|`.scb` 파일의 레이아웃과 **스키마가 바뀌었을 때의 보장** — 컬럼 태그, 타입 승격, 배포 전 검사. **프로토버프 와이어 포맷에서 가져온 것과 바꾼 것**|
|[**언어별 가이드**](doc/languages/readme.md)|생성된 코드를 프로젝트에 넣고 쓰는 법. 언어마다 준비물·주의사항·트러블슈팅이 다릅니다|
|[**트러블슈팅**](doc/troubleshooting.md)|변환이 실패했을 때 어디를 볼 것인가. 도구가 실제로 출력하는 메시지별로|
|[Summary와 히스토리](doc/history.md)|누가 언제 무엇을 바꿨는지 셀 단위로 추적하고 브라우저로 확인하기|
|[아키텍처와 개발](doc/architecture.md)|내부 구조, 패키징 주의점, 이 저장소에서 개발·테스트하는 법|
|[앞으로 할 것](doc/roadmap.md)|하려는 것과, 하지 않기로 한 것과 그 이유|

---

## Features

- 엑셀과 구글 스프레드시트를 둘 다 씁니다. 팀이 편한 쪽으로 고르면 되고, 한 프로젝트에서 섞어 써도 결국 하나로 합쳐집니다.
- 변환하면서 걸러낼 수 있는 실수는 최대한 걸러냅니다. 게임에서 문제가 생긴 뒤에 찾는 대신 변환할 때 알게 됩니다.
- 테이블끼리 참조할 수 있습니다. 같은 값을 여러 시트에 베껴 적지 않아도 됩니다.
- **데이터만 따로 패치**할 수 있습니다. 익스포트 결과를 CDN에 올려두면 생성된 업데이터가 바뀐 파일만 받아 최신으로 유지합니다 — 해시로 검증하고, 일시적인 장애는 재시도하고, 실패하면 이전 데이터를 그대로 둡니다. (C#·유니티·언리얼. `WriteUpdater` 옵션)
- 여러 언어로 뽑을 수 있습니다. C#(**.NET과 유니티 모두**), TypeScript, C++, C, Go, Rust, Python, Java, Kotlin, Ruby, PHP, Dart 코드와 언리얼 모듈을 생성합니다.
- 실제로 로드된 데이터를 HTML로 펼쳐 볼 수 있습니다. 값이 제대로 들어갔는지 눈으로 확인하고 넘어갈 수 있습니다.
- 파일(바이너리·JSON)로 내보내는 것 말고, MySQL / PostgreSQL / MongoDB / Redis에 바로 적재할 수도 있습니다.
- 서버와 클라이언트 중 한쪽에만 필요한 테이블과 컬럼은 그쪽 빌드에만 넣을 수 있습니다. (`TargetSide`)
- **누가 언제 무엇을 바꿨는지 셀 단위로 남고**, 웹 브라우저에서 볼 수 있습니다. (`--serve`)
- 문제가 생기면 어느 셀인지 짚어줍니다. 구글 시트라면 링크를 눌러 그 자리로 바로 갑니다.
- 시트의 문제를 한 번에 모아서 보고합니다. 하나 고치고 다시 돌리기를 반복하지 않아도 됩니다.
- **변환이 중간에 실패해도 이전 결과는 그대로 남습니다.** 파일은 스테이징 영역에 모았다가 마지막에 한꺼번에 옮기고, 데이터베이스는 섀도 테이블에 채운 뒤 통째로 바꿉니다.
- **읽는 쪽도 마찬가지입니다.** 이미 로드된 테이블을 다시 읽어도(데이터 패치·핫 리로드) 전부 읽고 참조까지 연결한 다음에 한 번에 교체합니다. 중간에 실패하면 **이전 데이터가 그대로 남고** 이유를 알려줍니다 — 빈 테이블이나 반쯤 채워진 테이블로 남는 일이 없습니다.

> 다만 **저장소 하나 단위**입니다. 파일 여러 개와 데이터베이스 여러 개를 한 트랜잭션으로 묶는 건 분산 트랜잭션 없이는 안 되므로, 각각이 따로 안전하게 바뀌도록 만들어져 있습니다.

---

## 시작하기

### 설치

[릴리즈](https://github.com/maxidea1024/SheetMan/releases)에서 내려받아 압축을 풀면 끝입니다. **.NET을 설치하지 않아도 됩니다** — 런타임이 실행 파일 안에 들어 있습니다.

|플랫폼|파일|
|--|--|
|Linux|`sheetman-<버전>-linux-x64.tar.gz` · `linux-arm64`|
|Windows|`sheetman-<버전>-win-x64.zip` · `win-arm64`|
|macOS|`sheetman-<버전>-osx-x64.tar.gz` · `osx-arm64` (애플 실리콘)|

터미널에서 받는 쪽이 편하면 아래를 그대로 붙여넣으세요. `VERSION`만 원하는 버전으로 바꾸면 됩니다.

**Linux · macOS**

```bash
VERSION=0.1.0
RID=linux-x64            # linux-arm64 · osx-x64 · osx-arm64 중 하나

curl -fsSL "https://github.com/maxidea1024/SheetMan/releases/download/v$VERSION/sheetman-$VERSION-$RID.tar.gz" \
  | tar -xz -C /usr/local/bin sheetman

sheetman --help
```

> `/usr/local/bin`에 권한이 없으면 `sudo`를 붙이거나, `-C ~/.local/bin`처럼 쓰기 가능한 곳으로 바꾸세요.
>
> macOS는 서명되지 않은 바이너리를 격리합니다. 한 번만 풀어주면 됩니다 —
> `xattr -d com.apple.quarantine /usr/local/bin/sheetman`

**Windows (PowerShell)**

```powershell
$Version = '0.1.0'
$Rid     = 'win-x64'      # 또는 win-arm64
$Dest    = "$env:LOCALAPPDATA\Programs\sheetman"

New-Item -ItemType Directory -Force $Dest | Out-Null
Invoke-WebRequest "https://github.com/maxidea1024/SheetMan/releases/download/v$Version/sheetman-$Version-$Rid.zip" -OutFile "$env:TEMP\sheetman.zip"
Expand-Archive "$env:TEMP\sheetman.zip" -DestinationPath $Dest -Force

# 이번 세션에서만. 계속 쓰려면 시스템 환경변수 PATH에 $Dest를 추가하세요.
$env:PATH = "$Dest;$env:PATH"
sheetman --help
```

**최신 버전을 자동으로** 집으려면 (`jq` 필요)

```bash
VERSION=$(curl -fsSL https://api.github.com/repos/maxidea1024/SheetMan/releases/latest | jq -r .tag_name)
VERSION=${VERSION#v}
```

**받은 파일 확인.** 릴리즈마다 `SHA256SUMS`가 함께 올라갑니다.

```bash
curl -fsSLO "https://github.com/maxidea1024/SheetMan/releases/download/v$VERSION/SHA256SUMS"
sha256sum -c SHA256SUMS --ignore-missing
```

<details>
<summary>소스에서 빌드하기</summary>

`.NET 10 SDK`가 필요합니다. 버전은 저장소 루트의 `global.json`에 고정되어 있습니다.

```
dotnet build SheetMan.slnx -c Release
```

</details>

### 실행

무엇을 어디서 읽어 어디로 내보낼지는 recipe 파일에 적습니다.

```
sheetman --new-recipe my-recipe.json --template unity   # 상황에 맞는 시작점
sheetman --recipe my-recipe.json                        # 변환
```

`--template`은 **그 상황에 필요한 설정만, 각각 왜 있는지 주석을 달아** 내놓습니다. 처음부터 백지로 시작하지 않아도 됩니다.

|템플릿|무엇을 위한 것|
|--|--|
|`unity`|유니티 클라이언트 — StreamingAssets + C#|
|`client-server`|같은 시트에서 클라이언트와 서버 두 벌|
|`web`|구글 스프레드시트 → TypeScript + JSON|
|`server`|게임 서버 — 데이터베이스 적재 + C++|
|`unreal`|언리얼 모듈|
|`ci`|변경 이력을 남기는 CI 변환|

`--template`을 생략하면 **모든 설정이 기본값으로 채워진** 파일이 나옵니다 — 무엇을 쓸 수 있는지 훑어볼 때.

자세한 것은 [CLI](doc/cli.md)와 [Recipe 파일](doc/recipe.md)을 보세요.

### 생성된 코드 쓰기

접근자 이름은 recipe의 `AccessorName`으로 정해집니다. 언어마다 준비물과 주의사항이 다르므로 [언어별 가이드](doc/languages/readme.md)에 각각 정리해 두었습니다.

```csharp
// C# — 정적입니다
await GameData.ReadAllAsync("./data");
var sword = GameData.Item.FindByIndex(1);
```

```typescript
// TypeScript
const tables = new Tables()
tables.readAllSync('./data')
const sword = tables.item.findByIndex(1)
```

```python
# Python
tables = Tables()
tables.read_all("./data")
sword = tables.item.find_by_index(1)
```

**참조는 로드 후 자동으로 연결됩니다.** `foreign` 필드는 파일에 인덱스로 저장되고, `readAll`이 모든 테이블을 읽은 뒤 실제 레코드 참조로 바꿔줍니다. (Rust만 예외 — [이유](doc/languages/rust.md#주의사항))

---

## 무엇을 만들어내는가

|종류|타깃|
|--|--|
|익스포트|`binary` `json`|
|데이터베이스|`mysql` `postgresql` `mongodb` `redis`|
|코드 생성|`csharp` `typescript` `cpp` `c` `unreal` `go` `rust` `python` `java` `kotlin` `ruby` `php` `dart`|
|문서|`html`|
|기록|`summary` `history`|

**따로 설치할 것이 없습니다.** 바이너리를 읽는 코드까지 출력 폴더에 같이 나오므로, 플러그인을 깔거나 include 경로를 잡을 일이 없습니다. Go는 `go.mod`, Rust는 `Cargo.toml`, 언리얼은 `Build.cs`까지 함께 나옵니다.

**타입 하나에 파일 하나입니다.** 시트에서 테이블을 지우면 그 파일도 없어집니다 — 헤더에 `Generated by SheetMan`이 적힌 파일 중 이번 실행이 쓰지 않은 것을 지우는 식입니다. 생성된 파일을 손으로 고쳐 쓰고 있다면 `"Sweep": false`로 꺼두세요.

---

## 검증

|게이트|하는 일|
|--|--|
|적합성 코퍼스|경계값 테이블 하나를 **12개 언어로 각각 컴파일·실행해서 읽고** 익스포터 JSON과 대조합니다|
|예약어 컴파일|키워드 이름 필드를 **12개 언어로 컴파일**합니다|
|헤더 단독 컴파일|C·C++ 헤더를 하나씩, 그 헤더만 include한 상태로 컴파일해 봅니다|
|C 헤더의 C++ 호환|`extern "C"`로 약속해놓고 못 쓰는 일이 없도록, C 헤더를 C++로도 컴파일합니다|
|Unreal|**실제 UnrealHeaderTool**에 통과시키고, 생성된 업데이터를 **실제 엔진의 UnrealBuildTool로 빌드·실행**합니다 (`SHEETMAN_UE_ROOT` 지정 시). 엔진 없이도 코퍼스를 읽어 익스포터와 대조합니다|
|생성 코드의 C# 수준|C# 리더를 `netstandard2.1`로 컴파일해, 유니티 2020.3이 받아들이는 C# 8을 넘지 않는지 확인합니다|
|골든 트리|워크북 변환 후 전 산출물 바이트 비교, 타임스탬프만 정규화|
|데이터베이스|`docker compose`로 네 엔진을 띄우고 적재한 뒤 서버에 직접 질의|
|웹서버|실제 포트에 띄우고 **API 응답과 CLI 출력을 바이트 단위로 비교**|
|self-contained 퍼블리시|CI가 매 실행마다 linux-x64로 퍼블리시하고, 그 산출물로 실제 변환을 돌립니다|

테이블 리더는 언어마다 별도 구현이라 어긋날 수 있습니다. 포맷을 정의하는 건 익스포터의 writer 하나이고, 13개의 테이블 리더는 그 하나를 각자 구현한 것입니다 — 그래서 회귀 스위트가 **C#으로 쓰고 12개 언어로 각각 읽어 대조**합니다. 실제로 이 방식이 `long`을 32비트로 잘라내던 writer 버그를 찾아냈습니다.

---

## 기여하기

버그와 제안은 [이슈](https://github.com/maxidea1024/SheetMan/issues)로 올려 주세요. 무엇을 어떻게 했을 때 그렇게 되는지가 있으면 가장 빠릅니다.

- 개발·테스트하는 법은 [아키텍처와 개발](doc/architecture.md)에 있습니다.
- 생성기나 템플릿을 건드렸다면 골든을 다시 기록하고 diff를 리뷰해 주세요. 방법은 같은 문서에 있습니다.
- 보안 문제는 공개 이슈 대신 [SECURITY.md](SECURITY.md)의 절차를 따라 주세요.

변경 내역은 [CHANGELOG.md](CHANGELOG.md)에 있습니다.

---

## References

- [Google.Apis.Sheets](https://github.com/googleapis/google-api-dotnet-client)
- [NPOI](https://github.com/nissl-lab/npoi)
- [Serilog](https://serilog.net/)
- [CommandLineParser](https://github.com/commandlineparser/commandline)
- [Netonsoft.Json](https://www.newtonsoft.com/json)

---

## 라이선스

[MIT](LICENSE).

생성된 코드와 함께 나오는 테이블 리더·업데이터도 같은 라이선스입니다. **생성물에 이 저장소의 라이선스를 표시할 의무는 없습니다** — 시트에서 나온 코드와 데이터는 그것을 만든 프로젝트의 것입니다.
