# Ruby

> [언어별 가이드로](readme.md) · [문서 목록으로](../../readme.md)

---

## 무엇이 생성되는가

```
<Path>/
  <AccessorName>.rb                접근자 — 여기서 나머지를 전부 require합니다
  tables/<table>_table.rb          테이블당 하나
  enums/<enum>.rb                  enum당 하나
  constants/<set>.rb               상수 세트당 하나
  sheetman/lite_binary_reader.rb   바이너리 리더 (함께 생성됩니다)
```

## 필요한 것

|항목|값|
|--|--|
|Ruby|3.2로 검증|
|gem|**없음**|

## recipe 설정

```jsonc
"Targets": [
  {
    "Type": "ruby",
    "Path": "lib/gamedata",
    "ModuleName": "GameData",       // 감싸는 모듈
    "AccessorName": "sheetman_data",// 접근자 파일 이름
    "BinaryTableFileExtension": ".table",
    "Sweep": true,
    "TargetSide": "s"
  }
]
```

## 쓰는 법

접근자 파일 하나만 require하면 나머지는 따라옵니다.

```ruby
require_relative 'lib/gamedata/sheetman_data'

tables = GameData::Tables.new
tables.read_all('./data')

sword = tables.item.find(1)
if sword
  # 참조는 로드 후 실제 레코드로 연결됩니다.
  puts "#{sword.name} / #{sword.category_id.name}"
end

tables.item.records.each { |row| }
```

확장자는 기본 인자입니다.

```ruby
tables.read_all('./data', '.bytes')
```

## 주의사항

**오토로더가 없습니다.** Ruby에는 이 상황에서 쓸 오토로더가 없으므로 접근자가 모든 조각을 `require_relative`합니다. 그래서 소비자는 파일 하나만 require하면 됩니다.

**테이블 파일은 enum을 require하지 않습니다.** Ruby의 enum은 정수 상수로 생성되므로 테이블 파일이 enum 타입을 이름 부르지 않습니다 — 그래서 테이블의 require 목록은 리더 하나뿐입니다.

**멤버 이름은 snake_case입니다.** Ruby 키워드도 거의 소문자라 충돌 가능성이 가장 높은 언어 중 하나입니다. 부딪히면 이스케이프됩니다.

## 트러블슈팅

|증상|원인과 조치|
|--|--|
|`cannot load such file`|`require_relative`는 부르는 파일 기준입니다. 절대 경로나 `$LOAD_PATH`를 쓰세요|
|`uninitialized constant GameData::Tables`|접근자 파일을 require하지 않았습니다. 조각 파일 하나만 require해도 나머지는 안 따라옵니다|
|참조가 `nil`|`read_all` 대신 테이블 하나만 읽었거나, 시트가 그 셀에 `0`을 넣었습니다|
