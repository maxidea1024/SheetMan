# PHP

> [언어별 가이드로](readme.md) · [문서 목록으로](../../readme.md)

---

## 무엇이 생성되는가

```
<Path>/
  <AccessorName>.php               접근자 — 여기서 나머지를 전부 require합니다
  tables/<Table>Table.php          테이블당 하나
  enums/<Enum>.php                 enum당 하나 (PHP 8.1 backed enum)
  constants/<Set>.php              상수 세트당 하나
  sheetman/LiteBinaryReader.php    바이너리 리더 (함께 생성됩니다)
```

## 필요한 것

|항목|값|
|--|--|
|PHP|8.1 이상 — `enum`을 씁니다|
|확장|**없음.** 기본 빌드로 동작합니다|
|Composer|**필요 없습니다.** 오토로더 없이 `require_once`로 엮여 있습니다|

## recipe 설정

```jsonc
"Targets": [
  {
    "Type": "php",
    "Path": "src/GameData",
    "Namespace": "MyGame\Data",
    "AccessorName": "GameData",
    "BinaryTableFileExtension": ".table",
    "Sweep": true,
    "TargetSide": "s"
  }
]
```

## 쓰는 법

접근자 파일 하나만 require하면 나머지는 따라옵니다.

```php
require_once __DIR__ . '/src/GameData/GameData.php';

use MyGame\Data\GameData;

$data = new GameData();
$data->readAll('./data');

$sword = $data->item->find(1);
if ($sword !== null) {
    // 참조는 로드 후 실제 레코드로 연결됩니다.
    echo $sword->name . ' / ' . $sword->categoryId->name;
}

foreach ($data->item->records as $row) { /* ... */ }
```

확장자는 기본 인자입니다.

```php
$data->readAll('./data', '.bytes');
```

## 주의사항

**오토로더가 없습니다.** 이 생성물은 Composer를 전제하지 않으므로 각 파일이 자기가 쓰는 것을 `require_once`합니다 — 테이블 파일은 리더와 자기 필드가 쓰는 enum을, 접근자는 전부를. 그래서 소비자는 파일 하나만 require하면 됩니다. Composer 프로젝트에 넣어도 충돌하지 않습니다.

**`bigint`는 `int`입니다.** PHP의 `int`는 64비트 플랫폼에서 64비트입니다. 리더는 `unpack('P')`를 쓰지 않는데, 그것이 2^63을 넘으면 float가 되기 때문입니다 — 32비트 두 개로 나눠 조립합니다.

**예약어는 이스케이프하지 않습니다.** PHP 7.0부터 프로퍼티와 메서드 이름에 예약어를 쓸 수 있습니다. 회귀 스위트가 키워드 이름 필드로 실제 파싱해서 확인합니다.

**`datetime`과 `timespan`은 `int`입니다.** .NET 틱이 그대로 들어옵니다.

## 트러블슈팅

|증상|원인과 조치|
|--|--|
|`Class "MyGame\Data\GameData" not found`|접근자 파일을 require하지 않았습니다. 조각 파일 하나만 require해도 나머지는 안 따라옵니다|
|`syntax error, unexpected 'enum'`|PHP 8.1 미만입니다|
|`bigint` 값이 부정확|8.0 이하 32비트 빌드일 수 있습니다. 64비트 PHP를 쓰세요|
|참조가 `null`|`readAll` 대신 테이블 하나만 읽었거나, 시트가 그 셀에 `0`을 넣었습니다|
