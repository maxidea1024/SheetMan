# Rust

> [언어별 가이드로](readme.md) · [문서 목록으로](../../readme.md)

---

## 무엇이 생성되는가

```
<Path>/
  Cargo.toml              WriteCargoToml이 true일 때
  src/lib.rs              모듈 트리와 재수출
  src/tables.rs           접근자
  src/<table>_table.rs    테이블당 하나
  src/enum_<enum>.rs      enum당 하나
  src/<set>.rs            상수 세트당 하나 (모듈 이름이 곧 경로)
  src/sheetman.rs         바이너리 리더 (함께 생성됩니다)
```

`lib.rs`가 `mod`를 선언하고 `pub use`로 전부 재수출하므로, 소비자가 쓰는 경로는 타입이 어느 파일에 있는지와 무관합니다 — `gamedata::ItemRecord`입니다.

## 필요한 것

|항목|값|
|--|--|
|Rust|edition 2021 (생성되는 `Cargo.toml`이 선언합니다)|
|크레이트 의존성|**없음.** 리더는 core와 std만 씁니다 — 레지스트리 접근 없이 빌드됩니다|

## recipe 설정

```jsonc
"Targets": [
  {
    "Type": "rust",
    "Path": "crates/gamedata",
    "CrateName": "gamedata",
    "WriteCargoToml": true,   // 이미 크레이트 안에 넣는다면 false
    "Edition": "2021",
    "BinaryTableFileExtension": ".table",
    "Sweep": true,
    "TargetSide": "s"
  }
]
```

## 프로젝트에 넣기

워크스페이스 멤버로 추가하거나, 경로 의존성으로 가리키세요.

```toml
[dependencies]
gamedata = { path = "crates/gamedata" }
```

이미 있는 크레이트 안에 넣는다면 `"WriteCargoToml": false`로 두고 `Path`를 그 크레이트의 루트로 지정하세요 — 생성물이 `src/` 아래로 들어갑니다.

## 쓰는 법

```rust
use gamedata::Tables;
use std::path::Path;

let mut tables = Tables::default();
tables.read_all(Path::new("./data"))?;

if let Some(sword) = tables.item.find(1) {
    println!("{}", sword.name);

    // 참조는 인덱스입니다. 직접 찾으세요.
    if let Some(category) = tables.item_category.find(sword.category_id_index) {
        println!("{}", category.name);
    }
}

for row in tables.item.records() { /* ... */ }
```

기본 인자가 없으므로 확장자는 짝이 되는 메서드입니다.

```rust
tables.read_all_with_extension(Path::new("./data"), ".bytes")?;
```

## 주의사항

**참조는 인덱스로 남습니다. Rust만 그렇습니다.** 레코드가 서로를 참조하면 그래프가 되는데 Rust는 한 레코드가 이웃을 소유하는 구조를 허용하지 않습니다. 대안은 생성 타입 전부에 수명을 꿰거나 행마다 참조 카운트 셀을 두는 것인데, 인덱스를 남기고 `find`를 부르게 하는 편이 읽기 쉽고 호출 한 번이면 됩니다.

필드 이름은 `<name>_index`입니다 (`category_id_index`).

**미사용 import가 없습니다.** 파일마다 그 파일이 쓰는 `use`만 적습니다. 크레이트 전체에 `#![allow(dead_code)]`와 `#![allow(clippy::all)]`이 걸려 있지만 미사용 import는 그 대상이 아닙니다 — 생성물은 경고 없이 빌드됩니다.

**멤버 이름은 snake_case입니다.** Rust 키워드와 부딪히면 뒤에 밑줄이 붙습니다 (`type` → `type_`). raw identifier(`r#type`)를 쓰지 않은 이유는 `crate`·`self`·`super`·`Self`가 raw가 될 수 없어서, 항상 통하는 규칙 하나가 거의 통하는 규칙 둘보다 낫기 때문입니다.

## 트러블슈팅

|증상|원인과 조치|
|--|--|
|`unresolved import gamedata::...`|`lib.rs`가 재수출하는 이름인지 확인하세요. 파일 이름이 아니라 타입 이름입니다|
|`no method named category_id`|참조는 인덱스로 남습니다. `category_id_index`와 `find`를 쓰세요|
|`Cargo.toml`이 덮어써짐|`"WriteCargoToml": false`로 두세요|
|`unused import` 경고|생성물에서 나면 버그입니다|
