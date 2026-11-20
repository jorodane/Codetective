Codetective 공통 데이터 편집

1. Unity 에셋 만들기
Project 창의 Create > Codetective > Data에서 World, Current Information,
Concept, Keyword, Physical, Place, Person, Item, Tool, Event, Organization,
Statement, Photo, Time, Relationship, Viewer를 생성합니다.

각 자료의 Instance Id는 세계 대상의 고정 식별자입니다. Display Name은 표시명이며
치환 참조 이름과 별개입니다. 배포한 콘텐츠의 ID는 변경하지 않는 것을 권장합니다.
Display Name (Fallback)은 원문/미번역 시 기본값입니다. 번역된 이름을 사용하려면
Display Name Key에 sample.victor.name 같은 키를 넣고 Text Table 에셋에 같은 key와
언어 코드(ko, en, ja 등), 문구를 등록합니다. Text Table은 World 또는 Scenario의
Text Tables에 연결합니다. 번역 키가 없으면 기존 문자열을 그대로 표시합니다.

설명·직업·진술 내용 등은 Text Keys의 Field에 description / occupation / content 같은
문자열 필드 코드를 지정하고 Key를 연결합니다. Viewer의 Title Key, 스키마의 Display
Name Key, 사용자 필드의 Label Key도 번역할 수 있습니다. 초기 관측의 Source Key와
Statement Key는 출처와 진술 문구를 번역합니다. 일반 Datum에는 textKey / labelKey가
있어 프로토콜 결과·배열·사전에도 번역 키를 유지할 수 있습니다.

Display Language는 표시할 콘텐츠의 언어 코드이며 Command Language와 독립적입니다.
Scenario의 Display Language를 비워 두면 World의 표시 언어를 사용합니다.
명령 식별자·별칭·플레이어의 참조 이름은 번역하지 않습니다. 기존 Names 배열은 명령
어휘용입니다. 표시명에 공백/기호가 있더라도 명령 문법을 바꾸지 않습니다. 저장 파일은
키와 원문을 보존하며, 불러온 뒤 현재 번역표로 표시합니다. 플레이어가 입력한 메모나
문자열을 임의로 번역하지 않습니다. 지역 번역(en-GB)이 없으면 상위 코드(en), ko,
원문 순으로 표시합니다. 기존 메뉴·대사 전체를 자동 번역하는 기능은 아닙니다.

Schema를 비워 두면 자료형의 기본 스키마를 사용합니다. 사용자 스키마를 지정하면
해당 기본 자료형을 상속해야 합니다. Fields / Functions는 인스턴스별 추가 멤버,
Initial Values는 생성된 기본값을 덮어쓰는 값입니다. 함수·변수의 Access는 기존
스토리 트리거와 퀘스트 조건을 그대로 사용합니다. 플레이어의 new/malloc은 없습니다.

World의 Objects에 대상을 연결합니다. 소지품·소유자·장소 연결 같은 순환 참조는
허용되며, 간접 연결된 에셋도 함께 등록됩니다. Event의 Subevents는 순환할 수 없습니다.
Occurred Events는 실제 발생한 사건입니다. 추측한 사건은 여기에 넣지 않습니다.
Current Time은 DAY 1의 실제 날짜와 시작 시각(09:00~18:00)을 지정합니다.
KernelScenarioDefinition의 World Data에 World를 연결하면 기존 게임과 함께 사용됩니다.
World의 Include Clinic Defaults는 World 단독 검증/생성 시 기본 상담소를 포함하는 옵션입니다.

2. 세계와 획득 정보
World의 값은 실제 상태입니다. Initially Known을 끄면 발견 전에도 세계에 존재합니다.
Current Information의 Discoveries는 대상의 존재를 알립니다. Observe Fields를 켜는 것은
해당 시점의 접근 가능한 값을 관측한다는 명시적 설정입니다. 참조 대상을 발견했다고
그 대상의 모든 세부 정보가 공개되는 것은 아닙니다.

Entries는 획득한 정보입니다. Read World Field를 켜면 실제 현재 값을 관측합니다.
끄면 Value 또는 Reference Value를 별도로 지정하여 세계 상태와 다른 보고를 만듭니다.
Source / Source Object는 출처, Observed At은 관측 시각, About Time은 내용이 다루는
시각입니다. 빈 Observed At(모두 0)은 현재 시각을 사용하고, 빈 About Time은 생략합니다.
수신 시각은 시스템이 기록합니다.
기존 관측은 지우지 않으며 마지막으로 관측된 시각을 기준으로 현재 정보를 선택합니다.
과거 보고가 늦게 도착하더라도 더 최근 관측을 덮어쓰지 않습니다.

Statement는 실제로 발화한 내용·화자·소재·시각을 기록합니다. 내용의 진위를 판정하거나
그 주장을 세계 값에 대입하지 않습니다. 런타임 RecordStatement는 발화 사실을 만들고,
보고 여부를 별도로 지정합니다. Inferences는 추론/조합 정보를 생성하며 실제 세계
대상을 만들지 않습니다. References는 이미 발견한 대상 또는 Inference ID에 치환 이름을
연결합니다. 참조의 변경·해제는 세계 대상을 변경·제거하지 않습니다.

3. Viewer
자료형마다 기본 Viewer가 있습니다. Viewer 에셋은 인스턴스 또는 스키마에 지정하며
부모 스키마의 Viewer도 상속합니다. Fields에 필드 코드를 나열하면 표시 순서를 정할
수 있습니다. 비워 두면 접근 가능한 멤버를 사용합니다. Hide Unknown을 끄면 미확인
칸을 표시하지만 비공개 값은 제공하지 않습니다. 커널의 확보 자료에서 조회합니다.
사진은 Photo의 Image에 Sprite를 지정합니다. Photo의 image 정보를 실제로 획득한
경우에만 표시합니다. 기존 Character/Tool Profile 에셋도 계속 사용할 수 있습니다.
월드 전체를 읽는 World API는 제작용이며, UI는 CurrentInformation/ViewData를 사용합니다.

4. Excel (.xlsx)
World/자료 에셋 우클릭 > Codetective > Export Data Excel 또는
Tools > Codetective > Data Excel을 엽니다. World와 연결된 자료·스키마·Viewer를
하나의 파일로 내보냅니다. Tools > Codetective > Create Data Example로 새 예제를
생성할 수 있고, Examples/CommonData에도 예제 에셋과 Example.xlsx가 있습니다.

Assets 시트: 에셋 목록. Key는 파일 내부 연결 키, Id는 런타임 ID,
Guid는 기존 Unity 에셋을 갱신할 때 쓰는 GUID입니다. 새 행은 Guid를 비웁니다.
자료형별 시트: 에셋마다 한 행. 일반 값과 단일 에셋 참조를 편집합니다.
Details 시트: 배열·복합 데이터·함수·조건을 경로별로 편집합니다.
Translations 시트: Text Table별로 key / language / text를 한 행씩 편집합니다.
같은 key의 ko/en 행을 추가할 수 있으며 같은 key+언어의 중복은 거부합니다.
예: functions[0].result.text, fields[0].access.story.triggersOn[0]
array 행의 Value는 배열 길이입니다. 항목을 늘리거나 줄이면 길이도 함께 수정합니다.
object/null 행은 복합 데이터의 존재 여부입니다. 연결된 에셋 내부를 경로로 수정하지
말고 해당 자료형 시트에서 수정합니다. 새 에셋은 Assets와 자료형 시트에 모두 추가합니다.

@대상Key는 파일 내부의 에셋 연결입니다. asset:GUID:FileID는 프로젝트의 외부 사진 등
에셋 연결입니다. 이미지는 파일에 포함하지 않습니다. 다른 프로젝트로 옮길 때는 외부
에셋도 옮기거나 참조를 수정해야 합니다. ~null은 빈 값이며 문자 그대로 ~로 시작하는
문자열에는 ~를 하나 더 붙입니다. 수식은 캐시된 결과만 읽으므로 Excel에서 계산 후
저장하세요. 내보내는 문자열은 수식으로 실행되지 않습니다.

Excel 읽기 / 검증은 임시 복사본만 수정합니다. 검증 완료 후 가져오기 적용을 누르면
기존 GUID를 유지하여 갱신하고 새 에셋은 지정 폴더에 생성합니다. 잘못된 ID·연결·자료형·
순환 세부사건·배열 인덱스는 적용 전에 거부합니다. 파일에서 빠진 기존 에셋은 삭제하지
않습니다. 적용은 Unity Undo를 지원합니다.

예제는 별도의 World입니다. 메인 시나리오에 자동으로 합치지 않습니다.
