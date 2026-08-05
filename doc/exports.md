# 내보내기

바이너리·JSON 파일과 데이터베이스 적재.

> [문서 목록으로](../readme.md)

---

## Export

임포트되고 가공된 데이터를 다양한 익스포터를 통해서 익스포트가 가능합니다.

|대상|설명|
|--|--|
|Binary|자체 포맷(LiteBinary) 바이너리 파일|
|Json|`.json` 파일. 이름 있는 형식과 배열만 담는 compact 형식을 선택할 수 있습니다.|
|MySql|MySQL로 직접 적재합니다.|
|PostgreSql|PostgreSQL로 직접 적재합니다.|
|MongoDB|MongoDB로 직접 적재합니다. 테이블당 컬렉션 하나, 로우당 도큐먼트 하나.|
|Redis|Redis로 직접 적재합니다. 로우당 해시 하나에 테이블당 인덱스 셋 하나.|

### JSON의 64비트 정수

`bigint` 값은 **문자열로** 기록됩니다.

```json
{ "index": 1, "startGold": "9007199254740993" }
```

JSON에는 숫자 타입이 하나뿐이고 대부분의 리더가 그것을 double로 다룹니다. `9007199254740993`을 그대로 쓰면 JavaScript는 `JSON.parse` 시점에 `9007199254740992`로 조용히 바꿔놓습니다. 더 나쁜 건 이 오류가 잘 드러나지 않는다는 점입니다 — 리터럴과 비교해봐도 그 리터럴 역시 같은 값으로 파싱되므로 양쪽이 "일치"합니다.

문자열로 기록하면 정확히 복원할 수 있고, 생성된 TypeScript는 이를 `BigInt`로 되살립니다. Protocol Buffers의 JSON 매핑이 int64에 대해 같은 선택을 하는 것과 같은 이유입니다.

`float` 값은 JSON에 왕복 가능한 최단 십진수로 기록되지만, JavaScript에는 32비트 부동소수점 타입이 없어 double로 넓어집니다. 생성된 TypeScript는 `Math.fround`로 다시 32비트 정밀도로 맞추므로, JSON 경로와 바이너리 경로가 같은 값을 냅니다.

### 데이터베이스 적재

네 대상 모두 **섀도 테이블에 적재한 뒤 원자적으로 교체**합니다. 적재 중 실패하면 기존 데이터가 그대로 남습니다.

|대상|교체 방식|
|--|--|
|MySQL|DDL 롤백이 불가하므로 다중 페어 `RENAME TABLE`(원자적)|
|PostgreSQL|DDL이 트랜잭션이므로 적재와 교체 전체를 단일 트랜잭션으로|
|MongoDB|`renameCollection(dropTarget)`|
|Redis|`MULTI`/`EXEC` 안에서 키 단위 `RENAME`|

타입 매핑에서 배열은 관계형 DB에서 `JSON`/`jsonb`가 되고, `timespan`은 정확도 유실을 피하기 위해 tick 값을 `BIGINT`로 저장합니다. 기본 인덱스 필드는 primary key(MongoDB는 `_id`)가 됩니다.

#### 자격증명

연결 문자열은 `${환경변수}` 형태의 치환을 지원합니다.

```json
"MySql": [
  {
    "ConnectionString": "Server=db;Database=game;Uid=sheetman;Pwd=${DB_PASSWORD}",
    "NamePrefix": "sm_"
  }
]
```

**비밀값을 recipe 파일에 직접 적지 마세요.** recipe는 버전관리에 커밋되므로 히스토리에 영구히 남습니다. 지정한 환경변수가 설정되어 있지 않으면 빈 문자열로 치환하지 않고 오류로 처리합니다.
