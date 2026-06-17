# NE MJCF Importer 패키지 설명

`com.ne.mjcf-importer`는 MuJoCo MJCF 로봇 XML을 Unity의
`ArticulationBody` 기반 `GameObject` 계층으로 가져오는 Unity 패키지입니다.

## 패키지 위치

- 패키지 경로: `Packages/com.ne.mjcf-importer`
- 패키지 이름: `com.ne.mjcf-importer`
- 대상 Unity 버전: `6000.0`
- Runtime 어셈블리: `Ne.MjcfImporter`
- Editor 어셈블리: `Ne.MjcfImporter.Editor`

## 가져오기 흐름

- `.mjcf` 파일은 `MjcfScriptedImporter`를 통해 자동으로 임포트됩니다.
- `.xml` MJCF 파일은 Unity 메뉴에서 명시적으로 선택해 임포트합니다.
- 메뉴 경로: `Assets > NE > Import MJCF XML`
- XML 임포트 결과는 `Assets/MJCFGenerated/<모델이름>/` 아래에 생성됩니다.
- 결과물에는 로봇 프리팹, 생성된 primitive mesh asset, `MjcfRobotAsset`
  데이터 asset이 포함됩니다.

## 좌표계 변환

패키지는 MJCF robotics 관례를 Unity 좌표계로 고정 변환합니다.

- MJCF 관례: `x-forward / y-left / z-up`
- Unity 관례: `x-right / y-up / z-forward`
- 벡터 변환: `Unity(x, y, z) = MJCF(-y, z, x)`

이 변환은 다음 값에 적용됩니다.

- body 위치
- joint 축과 joint 위치
- geom 위치
- geom `fromto` endpoint
- box half extents

회전은 먼저 MJCF local frame 회전으로 해석한 뒤 Unity basis로 변환합니다.
지원하는 회전 입력은 `quat`, `axisangle`, `euler`, `xyaxes`, `zaxis`이며,
`compiler eulerseq`도 읽습니다.

## 지원하는 MJCF 범위

핵심 로봇 구조 지원 범위는 다음과 같습니다.

- `<mujoco>`, `<compiler angle>`, `<compiler eulerseq>`
- `<default>` class lookup과 body `childclass`
- `<worldbody>`와 중첩 `<body>` 계층
- body `pos`, `quat`, `euler`, `axisangle`, `xyaxes`, `zaxis`
- joint `hinge`, `slide`, `ball`, `freejoint`
- joint `pos`, `axis`, `range`, `limited`, `stiffness`, `damping`
- 한 body 안의 multiple joints를 중간 `ArticulationBody` 노드 체인으로 생성
- primitive geom: `box`, `sphere`, `capsule`, `cylinder`
- geom `pos`, 회전 attribute, `size`, `fromto`
- 기본 mass/density를 `ArticulationBody.mass`에 반영
- actuator metadata 보존과 position/velocity actuator 런타임 제어

v1에서 제외한 범위는 다음과 같습니다.

- mesh geom import
- 고급 material/texture 처리
- tendon, equality, sensor 요소
- contact solver parameter의 정확한 재현
- motor actuator를 force/torque로 적용하는 기능
- MuJoCo 물리 결과의 1:1 재현

## Primitive Geometry 의미

importer는 Unity mesh/collider를 만들기 전에 MJCF primitive 의미를 먼저
정규화합니다.

- `sphere size="r"`: `r`은 radius입니다.
- `box size="x y z"`: 값은 MJCF half extents입니다. Unity collider size는
  `2 * halfExtents`입니다.
- `cylinder size="r h"`: `r`은 radius, `h`는 cylinder half-height입니다.
  Unity height는 `2 * h`입니다.
- `capsule size="r h"`: `r`은 radius, `h`는 cylinder 부분의 half-height입니다.
  Unity 전체 height는 `2 * h + 2 * r`입니다.
- `capsule fromto="x1 y1 z1 x2 y2 z2" size="r"`: 두 endpoint가 capsule의
  중심선 segment를 정의합니다. Unity 전체 height는 `distance + 2 * r`입니다.
- `cylinder fromto="x1 y1 z1 x2 y2 z2" size="r"`: 두 endpoint가 cylinder의
  중심선 segment를 정의합니다. Unity height는 `distance`입니다.

Unity 내부 primitive 기준축은 Y-axis로 통일합니다. `fromto` geom은 endpoint
중간점을 local center로 사용하고, Y-axis primitive를 endpoint 방향으로 회전합니다.

## Actuator 제어

root `GameObject`에는 `MjcfActuatorController`가 붙습니다.

```csharp
controller.SetControl("hip_position", 30.0f);
controller.SetControl(0, 1.0f);
```

지원하는 actuator 적용 방식은 다음과 같습니다.

- `<position>` actuator: `ArticulationDrive.target`을 갱신합니다.
- `<velocity>` actuator: `ArticulationDrive.targetVelocity`를 갱신합니다.
- `gear`는 입력값에 곱해 target 또는 target velocity로 변환합니다.
- `ctrllimited="true"`와 `ctrlrange`가 있으면 입력값을 clamp합니다.
- `<motor>` actuator는 asset metadata에는 남기지만 force/torque로 적용하지 않습니다.

## 테스트

EditMode 테스트는 `Packages/com.ne.mjcf-importer/Tests/Editor`에 있습니다.

확인하는 주요 항목은 다음과 같습니다.

- 좌표계 벡터/회전 변환
- `fromto` capsule/cylinder 길이와 방향
- MJCF capsule/cylinder/box size 의미
- joint `pos` anchor 반영
- multiple joints body의 wrapper chain 생성
- `compiler eulerseq`, `xyaxes`, `zaxis` 회전 파싱
- position/velocity actuator 파싱과 런타임 drive 갱신
- motor actuator no-op 처리

Unity batchmode 예시:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.0.32f1\Editor\Unity.exe" `
  -batchmode `
  -projectPath "C:\Users\jungle\Desktop\unity_Robotic_Sentis" `
  -runTests `
  -testPlatform EditMode `
  -testResults "C:\Users\jungle\Desktop\unity_Robotic_Sentis\TestResults.xml" `
  -quit
```

## 참고

이 패키지는 v1에서 Unity에서 확인 가능한 ArticulationBody robot hierarchy를
만드는 데 초점을 둡니다. MuJoCo와 완전히 동일한 solver behavior를 재현하는 것은
목표가 아닙니다.
