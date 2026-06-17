Unity MJCF Importer 기획서
1. 목표

MJCF 파일을 Unity 프로젝트에 import하면, MJCF의 body, joint, geom 구조를 Unity의 GameObject + ArticulationBody + Collider + MeshRenderer 구조로 자동 변환한다.

MJCF .mjcf
→ GameObject hierarchy
→ ArticulationBody chain
→ default geom visual
→ collider
→ joint limit / stiffness / damping 반영
2. 패키지 구조
Packages/
  com.ne.mjcf-importer/
    package.json

    Runtime/
      Ne.MjcfImporter.asmdef
      MjcfRobotAsset.cs
      MjcfTypes.cs

    Editor/
      Ne.MjcfImporter.Editor.asmdef
      MjcfScriptedImporter.cs
      MjcfParser.cs
      ArticulationRobotBuilder.cs
      PrimitiveMeshBuilder.cs
      MjcfImportSettings.cs

역할은 이렇게 나눈다냥.

Runtime
= import 결과 데이터를 담는 타입

Editor
= 실제 MJCF 파싱, GameObject 생성, Asset import 처리
3. Import 방식

Unity의 ScriptedImporter를 사용한다.

확장자:
.mjcf

.xml은 다른 XML 파일과 충돌할 수 있으므로, 우선 .mjcf 확장자를 사용한다.

[ScriptedImporter(1, "mjcf")]
public class MjcfScriptedImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        // 1. MJCF 파일 읽기
        // 2. XML 파싱
        // 3. Robot data 생성
        // 4. GameObject hierarchy 생성
        // 5. ArticulationBody / Collider / MeshRenderer 생성
        // 6. Import asset으로 등록
    }
}
4. 최종 생성 결과

humanoid.mjcf를 import하면 다음 구조가 생성된다.

Humanoid
 ├─ torso
 │   ├─ ArticulationBody
 │   ├─ MeshFilter
 │   ├─ MeshRenderer
 │   └─ Collider
 │
 ├─ thigh_l
 │   ├─ ArticulationBody
 │   ├─ MeshFilter
 │   ├─ MeshRenderer
 │   └─ Collider
 │
 └─ shin_l
     ├─ ArticulationBody
     ├─ MeshFilter
     ├─ MeshRenderer
     └─ Collider

필요하면 별도 데이터 에셋도 같이 만든다냥.

Humanoid_MjcfData.asset
5. 변환 규칙
MJCF	Unity
body	GameObject
body hierarchy	Transform parent-child
body pos	transform.localPosition
body quat/euler/axisangle	transform.localRotation
joint hinge	ArticulationBody.jointType = RevoluteJoint
joint slide	ArticulationBody.jointType = PrismaticJoint
joint ball	ArticulationBody.jointType = SphericalJoint
joint range	ArticulationDrive.lowerLimit / upperLimit
joint stiffness	ArticulationDrive.stiffness
joint damping	ArticulationDrive.damping
geom box	BoxCollider + cube mesh
geom sphere	SphereCollider + sphere mesh
geom capsule	CapsuleCollider + capsule mesh
geom cylinder	CapsuleCollider 또는 MeshCollider + cylinder mesh
geom mesh	v1 후순위
6. 핵심 원칙

MJCF의 body offset은 Unity Transform hierarchy에 고정된다.

body pos / rot
→ import 시 Transform local 값으로 고정

관절은 ArticulationBody가 담당한다.

joint type
joint axis
joint range
stiffness
damping

즉 importer는 런타임에서 움직임을 직접 제어하지 않는다냥.
움직일 수 있는 로봇 구조를 Unity scene/prefab으로 만들어주는 데까지만 담당한다.

7. v1 지원 범위
지원
body hierarchy
body pos
body quat
body euler
body axisangle

geom:
- box
- sphere
- capsule
- cylinder

joint:
- hinge
- slide
- ball

joint property:
- axis
- range
- stiffness
- damping
- limited

mass property:
- mass
- density 기본 반영
후순위
mesh geom import
material / texture
tendon
equality
sensor
contact pair / exclude
actuator 세부 모델
compiler / default class 완전 지원
MuJoCo와 1:1 동일 물리 재현
8. 주요 클래스
MjcfScriptedImporter

.mjcf 파일 import 진입점.

역할:
- 파일 읽기
- parser 호출
- builder 호출
- import 결과를 Unity asset으로 등록
MjcfParser

XML을 읽어 내부 데이터 구조로 변환한다.

역할:
- <mujoco> root 파싱
- <default> 파싱
- <worldbody> body 계층 파싱
- <geom> 파싱
- <joint> 파싱
- <asset> 일부 파싱
MjcfRobotAsset

파싱된 MJCF 정보를 보존하는 데이터 에셋.

포함:
- bodies
- joints
- geoms
- defaults
- assets
- original path
- import settings
ArticulationRobotBuilder

MjcfRobotAsset을 Unity GameObject hierarchy로 변환한다.

역할:
- body별 GameObject 생성
- Transform hierarchy 구성
- ArticulationBody 추가
- joint 설정 적용
- geom visual/collider 생성
PrimitiveMeshBuilder

MJCF primitive geom을 Unity mesh로 만든다.

지원:
- box mesh
- sphere mesh
- capsule mesh
- cylinder mesh
9. Import Inspector 옵션
Generate Visual Geometry      [✓]
Generate Colliders            [✓]
Apply Joint Limits            [✓]
Apply Stiffness / Damping     [✓]
Import Mesh Geoms             [ ]
Scale                         1.0
Stiffness Scale               1.0
Damping Scale                 1.0
Default Density               1000
10. 구현 순서
1. Unity package 생성
2. .mjcf ScriptedImporter 생성
3. 빈 GameObject import 성공 확인
4. MJCF XML parser 작성
5. body hierarchy 파싱
6. GameObject hierarchy 생성
7. body pos/rotation 적용
8. ArticulationBody 추가
9. hinge / slide / ball joint 설정
10. geom box/sphere/capsule/cylinder visual 생성
11. collider 생성
12. joint range 적용
13. stiffness/damping 적용
14. MjcfRobotAsset 생성 및 보존
15. 샘플 MJCF로 검증
11. 검증 목표
1. 단일 body + box geom import
2. parent-child body hierarchy import
3. hinge joint pendulum import
4. joint range limit 정상 작동
5. stiffness/damping drive 값 반영
6. capsule humanoid body import
7. import 결과가 prefab처럼 scene에 배치 가능
12. 주요 리스크
MJCF ↔ Unity 좌표계 변환
quat / euler / axisangle 변환
joint axis를 ArticulationBody drive 축에 맞추는 문제
capsule/cylinder fromto 처리
default class 상속 처리
mass / inertia 반영 정확도
mesh geom import 복잡도
MuJoCo 물리 파라미터와 Unity ArticulationDrive 의미 차이
13. 최종 정의
Unity MJCF Importer
= MJCF → ArticulationBody Robot Prefab Compiler

v1의 목표는 MJCF 파일을 읽어 Unity에서 바로 확인 가능한 ArticulationBody 기반 로봇 hierarchy를 생성하는 것이다냥.
ONNX 추론, policy 제어, 학습 연동은 importer 이후 단계로 분리한다.