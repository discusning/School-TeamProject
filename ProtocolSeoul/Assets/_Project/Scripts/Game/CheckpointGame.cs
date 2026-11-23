using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace ProtocolSeoul.Game
{
    // 검문 프로토타입. 화면 구역은 기획_추가_261115 "4. 플레이어 인터페이스" 골격 (그림·색은 임시).
    //   좌측: NPC 영역 (초상 · 판정 결과 띠 · 첫마디 · 대화 로그 [Tab])
    //   우측 상단: 서류 검토 영역 (작은 서류가 위에서 내려옴 → 클릭하면 확대)
    //             그날 규칙에 없는 서류(예: 검역 소견서)는 칸 자체를 보이지 않음 (3차 회의 261117)
    //             방문자가 안 낸 서류도 칸이 없음 — 그 서류가 없는 것으로 처리 (2026-11-23)
    //   우측 하단: 단말기 (주민등록 DB · 규칙 및 매뉴얼)
    //   우측: 판정 버튼 (통과 · 거부 · 제거 · 신고[5일차부터 보임])
    // 기획에 있으나 로직이 없는 기능은 자리만 두고 "준비 중"으로 표시.
    // 흐름: 하루 시작 → 방문자 → (검사 모드로 서류 칸 ↔ DB 칸 비교) → 통과/거부 → 맞음·틀림 → 다음 → 하루 정산 → 다음 날
    // 판정 기준은 Core/Rules.cs 한 곳. 이 파일은 보여 주고 입력만 받는다.
    public class CheckpointGame : MonoBehaviour
    {
        // Art: 항목 칸 그림 3상태 (0 기본 · 1 선택됨 노랑 · 2 불일치 빨강). 없으면 색으로 표시.
        class FieldCell { public Source Src; public Field Field; public Image Bg; public Color Normal; public Sprite[] Art; }

        static readonly Color BadColor = new(1f, 0.55f, 0.45f);

        static void Paint(FieldCell c, int state)
        {
            if (c?.Bg == null) return;
            if (c.Art != null && c.Art[state] != null) { c.Bg.sprite = c.Art[state]; c.Bg.color = Color.white; return; }
            c.Bg.color = state == 0 ? c.Normal : state == 1 ? Ui.Pick : BadColor;
        }

        // 받은 UI 그림 (C037). 항목 칸: 서류용 베이지 · DB용 초록
        static Sprite[] DocCellArt => CellArt("doc");
        static Sprite[] DbCellArt => CellArt("db");
        static Sprite[] CellArt(string k)
        {
            var a = new[] { Ui.Art($"cell_{k}_normal", 14), Ui.Art($"cell_{k}_pick", 14), Ui.Art($"cell_{k}_bad", 14) };
            return a[0] == null ? null : a;
        }
        const float PassAspect = 1536f / 1024f, DbAspect = 1598f / 984f;

        List<DayConfig> _days;
        int _dayIndex, _vi, _runTotal;
        ResidentDatabase _db;
        List<Visitor> _visitors = new();
        DayLedger _ledger;
        int _seed;

        bool _inspect, _decided;
        FieldCell _pickA;
        readonly List<FieldCell> _cells = new();
        FieldCell _boothCell;
        int _tab, _termTab;

        Text _header, _counter, _rule, _speech, _inspectLine, _inspectBtnText, _verdictText, _verdictWhy;
        Button _inspectBtn;
        Sprite[] _inspectArtOff, _inspectArtOn;
        Sprite _bandOk, _bandBad, _boothBg;
        Text _who;
        GameObject _bubble;

        // 판정 결과 띠를 띄우면 그 자리의 첫마디 칸은 숨김 (그림 띠일 때만. 첫마디는 대화 로그에 남음)
        void SetBand(bool on)
        {
            _verdictBand.gameObject.SetActive(on);
            if (_bandOk == null) return;
            _bubble.SetActive(!on);
            _who.gameObject.SetActive(!on);
        }

        RectTransform _boothFace, _docContent, _termContent;
        Image _verdictBand;

        // 대화 로그 (3차 회의 261117: 질문 선택창 대신, 왼쪽 화면을 검정 불투명으로 덮고 흰 글씨)
        RectTransform _logPanel, _logContent;
        ScrollRect _logScroll;
        bool LogOpen => _logPanel != null && _logPanel.gameObject.activeSelf;
        Button _passBtn, _denyBtn, _killBtn, _reportBtn, _nextBtn;
        const int ReportFromDay = 5; // 신고 버튼이 보이기 시작하는 날
        readonly List<Button> _termTabs = new();
        // 서류 종류 (작은 서류 칸 순서). 그날 규칙에 있는 서류만 칸이 생김
        enum Doc { Pass, Cert, Permit }
        readonly List<Doc> _docKinds = new();
        static string DocName(Doc d) => d switch { Doc.Pass => "임시 통행증", Doc.Cert => "검역 소견서", _ => "입성 사유서" };
        string _jinChoice; // 강00 이벤트: 진00 처분 (3일차 선택 결과 저장)

        void SetVerdictButtons(bool on) { _passBtn.interactable = _denyBtn.interactable = _killBtn.interactable = on; }
        static string Who(Visitor v) => v.PassMissing ? "(통행증 없음)" : v.Pass.Name;

        // 서류 연출 (3차 회의 261117): 작은 서류가 위에서 내려오고, 클릭하면 서류 검토 영역 가득 확대
        RectTransform _desk, _tray, _docView;
        readonly List<RectTransform> _smallDocs = new();
        Coroutine _dropCo, _zoomCo;
        Vector3? _docHome; // 축소 움직임 중 원래 자리 (중간에 끊겨도 되돌림)
        const float DropTime = 0.7f, DropStagger = 0.15f, DropFrom = 620f, ZoomTime = 0.18f;
        bool DocOpen => _docView != null && _docView.gameObject.activeSelf;
        static readonly Color Soon = new(0.6f, 0.6f, 0.6f);
        RectTransform _overlay, _canvas;

        AudioSource _audio;
        AudioClip _stamp, _buzz, _ding, _tick;

        Visitor V => _visitors[_vi];
        Resident Record => V.PassMissing ? null : _db.FindByName(V.Pass.Name);
        DayConfig Day => _days[_dayIndex];
        // 주민등록 DB가 있는 날 (1일차 DB 없음 → 2일차 복구, 1주차 스토리 261122)
        bool DbDay => Day.Has(ErrorKind.NotInDatabase) || Day.Has(ErrorKind.IdDigitMismatch) || Day.Has(ErrorKind.AddressMismatch) || Day.Has(ErrorKind.BirthMismatch);

        void Awake()
        {
            Ui.Font = Resources.Load<Font>("Fonts/NotoSansKR");
            _days = Resources.LoadAll<DayConfig>("Days").OrderBy(d => d.Day).ToList();
            if (_days.Count == 0) _days = DayConfig.Defaults();
            _db = ResidentDatabase.Generate(seed: 2050);
            _seed = ArgInt("-seed", Environment.TickCount & 0xffff);
            // 시연용: 항상 모니터 크기 전체 화면으로 시작 (지난번 창 크기 기억을 무시).
            // 자동 스크린샷(-shots)이나 -window 로 켜면 명령줄 크기를 그대로 쓴다.
            if (ArgStr("-shots") == null && Array.IndexOf(Environment.GetCommandLineArgs(), "-window") < 0)
                Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, FullScreenMode.FullScreenWindow);
            BuildAudio();
            BuildUi();
        }

        void Start()
        {
            string shots = ArgStr("-shots");
            if (shots != null) Application.runInBackground = true; // 창이 앞에 없어도 자동 촬영이 멈추지 않게
            ShowDayIntro();
            if (shots != null) StartCoroutine(ShotRun(shots));
        }

        void Update()
        {
            // 클릭한 버튼이 "선택됨"으로 남으면 Space가 그 버튼을 다시 누름 → 선택을 계속 비움
            var es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null) es.SetSelectedGameObject(null);
            FitScreen();
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.spaceKey.wasPressedThisFrame && _overlay == null) ToggleInspect();
            if (kb.tabKey.wasPressedThisFrame && _overlay == null) SetLog(!LogOpen);
            if (kb.escapeKey.wasPressedThisFrame && _overlay == null && DocOpen) { CloseDoc(); Play(_tick); }
            if (kb.f11Key.wasPressedThisFrame) ToggleFullScreen();
        }

        // F11: 전체 화면 ↔ 창 (창은 모니터의 80%, 16:9, 크기 조절 가능)
        static void ToggleFullScreen()
        {
            int w = Display.main.systemWidth, h = Display.main.systemHeight;
            if (Screen.fullScreenMode == FullScreenMode.Windowed)
            {
                Screen.SetResolution(w, h, FullScreenMode.FullScreenWindow);
                return;
            }
            int ww = Mathf.RoundToInt(Mathf.Min(w * 0.8f, h * 0.8f * 16f / 9f));
            Screen.SetResolution(ww, Mathf.RoundToInt(ww * 9f / 16f), FullScreenMode.Windowed);
        }

        // 화면 비율이 달라도 16:9 판(1920×1080 기준)을 통째로 키우고 줄임 → 남는 곳은 검은 띠.
        // 4:3·울트라와이드에서 칸이 눌려 글자가 줄바꿈되던 문제 방지.
        CanvasScaler _scaler;
        float _lastAspect;
        void FitScreen()
        {
            float aspect = Screen.width / (float)Mathf.Max(1, Screen.height);
            if (_scaler == null || Mathf.Approximately(aspect, _lastAspect)) return;
            _lastAspect = aspect;
            _scaler.matchWidthOrHeight = aspect < 16f / 9f ? 0f : 1f;
        }

        // ───────────── 하루 진행 ─────────────

        void ShowDayIntro()
        {
            var o = OpenOverlay();
            var panel = Ui.Box(o, "Panel", 0.25f, 0.2f, 0.75f, 0.8f, Ui.Booth);
            Ui.Label(panel.transform, "Title", 0.06f, 0.8f, 0.94f, 0.95f, $"{Day.Day}일차 — {Fmt.Date(Day.Today)}", 48, Ui.Light, TextAnchor.MiddleCenter);
            Ui.Shrink(Ui.Label(panel.transform, "Brief", 0.08f, 0.38f, 0.92f, 0.78f, Day.Briefing, 30, Ui.Light, TextAnchor.MiddleCenter), 18);
            Ui.Label(panel.transform, "Rule", 0.08f, 0.26f, 0.92f, 0.36f, "오늘의 규칙: " + Day.RuleShort, 26, Ui.Pick, TextAnchor.MiddleCenter);
            Ui.Button(panel.transform, "Start", 0.35f, 0.06f, 0.65f, 0.2f, "근무 시작", Ui.PassGreen, Color.white, 32, StartDay);
        }

        void StartDay()
        {
            CloseOverlay();
            _visitors = new VisitorGenerator(_db, _seed * 31 + Day.Day).GenerateDay(Day);
            _ledger = new DayLedger();
            _vi = 0;
            _header.text = $"{Day.Day}일차 · {Fmt.Date(Day.Today)}";
            _rule.text = "오늘의 규칙: " + Day.RuleShort;
            if (_dayIndex == 0) _jinChoice = null;
            _termTabs[0].gameObject.SetActive(DbDay); // 1일차: 주민등록 DB 없음 → DB 탭 숨김
            _reportBtn.gameObject.SetActive(Day.Day >= ReportFromDay); // 신고: 1~4일차엔 아예 안 보임
            // 대화 로그에 날짜 줄 없음 (대화로그 PDF 4·6쪽, C051)
            ShowVisitor();
        }

        void ShowVisitor()
        {
            _decided = false;
            SetInspect(false);
            _counter.text = $"방문자 {_vi + 1} / {_visitors.Count}";
            _speech.text = V.Line;
            FaceView.Draw(_boothFace, V.Face, _boothBg != null ? Color.clear : Ui.Booth * 1.2f, true); // 배경 그림이 있으면 얼굴 뒤로 검문소가 보임. 폭을 꽉 채우고 아래를 자름 (C050)
            _termTab = DbDay ? 0 : 1;
            CloseDoc();
            BuildSmallDocs();
            RebuildTerminal();
            SetBand(false);
            SetLog(false);
            // 대화 로그는 지금 검사 중인 방문자의 대화만 (1인 1로그, 대화로그 PDF 4·5·6쪽). 새 방문자가 오면 비움
            ClearLog();
            AddBubble(true, "플레이어", PlayerAsk);
            AddBubble(false, V.PassMissing ? "방문자" : V.Pass.Name, V.Line);
            SetVerdictButtons(true);
            _nextBtn.gameObject.SetActive(false);
            Play(_tick);
        }

        void Decide(Verdict chosen)
        {
            if (_decided || _overlay != null) return;
            _decided = true;
            _ledger.Record(V, chosen);
            if (V.Story == "jin")
            {
                // 강00 이벤트 (1주차 스토리 261122): 진00 처분을 선택 결과로 저장. 정답·오답으로 매기지 않음 (가정)
                _jinChoice = Name(chosen);
                Play(_stamp);
                ShowBand(true, $"선택 기록 — 진00 {Name(chosen)}", "강00 이벤트 선택이 저장됨");
                SetVerdictButtons(false);
                _nextBtn.gameObject.SetActive(true);
                return;
            }
            bool ok = chosen == V.Correct;
            Play(_stamp);
            Play(ok ? _ding : _buzz, 0.12f);
            string why = V.Correct == Verdict.Pass
                ? (ok ? "문제 없는 방문자" : "이 방문자는 문제가 없었습니다")
                : (ok ? "이유: " : "놓친 것: ") + Fmt.ErrorText(V.Reason);
            // 판정 결과: 초상 아래쪽 띠. 대화 로그에는 판정 줄 없음 (대화로그 PDF 4·6쪽, C051)
            ShowBand(ok, $"{(ok ? "정답" : "오답")} — 처리 {Name(chosen)} / 정답 {Name(V.Correct)}", why);
            SetVerdictButtons(false);
            _nextBtn.gameObject.SetActive(true);
        }

        void ShowBand(bool ok, string head, string why)
        {
            if (_bandOk != null)
            {
                _verdictBand.sprite = ok ? _bandOk : _bandBad;
                _verdictBand.color = Color.white;
                _verdictText.text = head;
                _verdictWhy.text = why;
            }
            else
            {
                _verdictBand.color = ok ? Ui.Solid(Ui.PassGreen * 0.85f) : Ui.Solid(Ui.DenyRed * 0.85f);
                _verdictText.text = $"{head}\n{why}";
            }
            SetBand(true);
        }

        void Next()
        {
            if (!_decided) return;
            _vi++;
            if (_vi >= _visitors.Count) ShowSettlement();
            else ShowVisitor();
        }

        void ShowSettlement()
        {
            _runTotal += _ledger.Total;
            var o = OpenOverlay();
            var panel = Ui.Box(o, "Panel", 0.18f, 0.06f, 0.82f, 0.94f, Ui.Booth);
            var t = panel.transform;
            bool fired = _ledger.Fired;
            Ui.Label(t, "Title", 0.05f, 0.9f, 0.95f, 0.98f, fired ? $"{Day.Day}일차 정산 — 퇴출" : $"{Day.Day}일차 정산", 46, fired ? new Color(1f, 0.5f, 0.45f) : Ui.Light, TextAnchor.MiddleCenter);
            bool hasJin = _ledger.Entries.Any(e => e.Visitor.Story == "jin");
            float top = hasJin ? 0.8f : 0.86f;
            float y = top, step = Mathf.Min(0.07f, (top - 0.36f) / Mathf.Max(1, _ledger.Entries.Count));
            foreach (var e in _ledger.Entries)
            {
                bool story = e.Visitor.Story == "jin";
                string mark = story ? "●" : e.Correct ? "○" : "×";
                string why = story ? "선택 기록 (강00 이벤트)" : e.Visitor.Correct == Verdict.Pass ? "문제 없음" : Fmt.ErrorText(e.Visitor.Reason);
                var col = story || e.Correct ? Ui.Light : new Color(1f, 0.55f, 0.5f);
                Ui.Label(t, "Mark", 0.06f, y - step, 0.1f, y, mark, 24, col, TextAnchor.MiddleCenter);
                Ui.Shrink(Ui.Label(t, "Name", 0.11f, y - step, 0.24f, y, Who(e.Visitor), 24, col), 14);
                Ui.Label(t, "Verdict", 0.24f, y - step, 0.5f, y, story ? $"처리 {Name(e.Chosen)}" : $"처리 {Name(e.Chosen)} · 정답 {Name(e.Visitor.Correct)}", 24, col);
                Ui.Shrink(Ui.Label(t, "Why", 0.5f, y - step, 0.95f, y, why, 24, col), 14);
                y -= step;
            }
            // 강00 이벤트 선택 (원문 선택 1~3). 선택 2·3은 4일차 강00 처분으로 갈림 → 4일차가 없어 저장만 함
            if (hasJin && _jinChoice != null)
            {
                string pick = _jinChoice == "통과" ? "진00을 들여보냄 (선택 2·3은 4일차에 갈림)" : "선택 1) 강00의 말을 믿고 진00을 거부·반려";
                Ui.Label(t, "Story", 0.06f, 0.82f, 0.94f, 0.88f, "강00 이벤트 — " + pick, 24, Ui.Pick, TextAnchor.MiddleCenter);
            }
            Ui.Label(t, "Sum", 0.06f, 0.2f, 0.94f, 0.34f,
                $"정답 {_ledger.CorrectCount} · 실수 {_ledger.Mistakes} → {_ledger.Stage}\n" +
                $"배급 +{_ledger.Earned}  벌점 −{_ledger.Penalty}  =  {_ledger.Total}   (누적 {_runTotal})", 28, fired || _ledger.RationStopped ? new Color(1f, 0.55f, 0.5f) : Ui.Pick, TextAnchor.MiddleCenter);
            Ui.Label(t, "Note", 0.06f, 0.13f, 0.94f, 0.2f, "실수 2번까진 경고, 3번부터 식량 감소, 4번부터 식량 미지급, 5번부터 퇴출(배드엔딩)\n※ 배급·벌점 숫자는 임시입니다 (기획 확정 전)", 20, new Color(0.7f, 0.7f, 0.7f), TextAnchor.MiddleCenter);
            bool last = fired || _dayIndex >= _days.Count - 1;
            Ui.Button(t, "Next", 0.35f, 0.03f, 0.65f, 0.12f, last ? "처음부터 다시" : "다음 날 ▶", Ui.PassGreen, Color.white, 30, () =>
            {
                CloseOverlay();
                if (last) { _dayIndex = 0; _runTotal = 0; } else _dayIndex++;
                ShowDayIntro();
            });
        }

        // ───────────── 검사 모드 (보고서 9-5) ─────────────

        void ToggleInspect() => SetInspect(!_inspect);

        void SetInspect(bool on)
        {
            _inspect = on;
            _pickA = null;
            foreach (var c in _cells.Append(_boothCell)) Paint(c, 0);
            if (_inspectArtOn != null) Ui.SetSprites(_inspectBtn, on ? _inspectArtOn : _inspectArtOff);
            _inspectBtnText.text = _inspectArtOn != null
                ? "검사 모드"
                : "검사 모드 [Space]";
            _inspectLine.text = on ? "검사 모드 — 서류 칸 하나, DB 칸 하나를 차례로 클릭" : "";
        }

        void OnCellClicked(FieldCell cell)
        {
            if (_overlay != null) return;
            // 검사 모드가 꺼져 있으면 확대된 서류의 칸을 눌러도 서류 다시 클릭으로 봄 → 축소
            if (!_inspect && DocOpen && IsDocSrc(cell.Src)) { ShrinkDoc(); return; }
            if (!_inspect) { _inspectLine.text = "칸을 비교하려면 검사 모드를 켜세요 [Space]"; return; }
            if (_pickA == null)
            {
                foreach (var c in _cells.Append(_boothCell)) Paint(c, 0);
                _pickA = cell;
                Paint(cell, 1);
                _inspectLine.text = $"「{Fmt.FieldName(cell.Field)}」 선택 — 비교할 칸을 하나 더 고르세요";
                Play(_tick);
                return;
            }
            if (_pickA == cell) { Paint(cell, 0); _pickA = null; _inspectLine.text = "선택 취소"; return; }

            var result = Inspector.Compare(V, Record, _pickA.Src, _pickA.Field, cell.Src, cell.Field);
            Paint(cell, 1);
            switch (result)
            {
                case CompareResult.Match:
                    _inspectLine.text = $"[일치] {Fmt.FieldName(cell.Field)}";
                    _inspectLine.color = new Color(0.55f, 1f, 0.6f);
                    Play(_ding);
                    break;
                case CompareResult.Mismatch:
                    _inspectLine.text = $"[불일치] {Fmt.FieldName(cell.Field)} — 두 칸이 다릅니다";
                    _inspectLine.color = new Color(1f, 0.5f, 0.45f);
                    Paint(_pickA, 2); Paint(cell, 2);
                    Play(_buzz);
                    break;
                default:
                    _inspectLine.text = "관련 없음 — 같은 종류의 칸끼리만 비교됩니다";
                    _inspectLine.color = Ui.Pick;
                    break;
            }
            _pickA = null;
        }

        // ───────────── 화면 만들기 ─────────────

        void BuildUi()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            _scaler = scaler;
            _canvas = (RectTransform)canvasGo.transform;
            FitScreen();
            if (FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            Ui.Box(_canvas, "Letterbox", 0, 0, 1, 1, Color.black);
            var root = Ui.Box(Frame(_canvas), "Root", 0, 0, 1, 1, Ui.Bg).transform;

            // 위쪽 바
            var bar = Ui.Box(root, "Header", 0, 0.955f, 1, 1, new Color(0.07f, 0.07f, 0.08f)).transform;
            _header = Ui.Label(bar, "Day", 0.01f, 0, 0.25f, 1, "", 26, Ui.Light);
            _counter = Ui.Label(bar, "Count", 0.25f, 0, 0.45f, 1, "", 26, Ui.Light, TextAnchor.MiddleCenter);
            _rule = Ui.Label(bar, "Rule", 0.45f, 0, 0.99f, 1, "", 24, Ui.Pick, TextAnchor.MiddleRight);

            // ① NPC 영역 (좌측): 초상(판정 결과 띠) · 첫마디 · 대화 로그 버튼
            var npcImg = Ui.Box(root, "NpcArea", 0, 0, 0.4f, 0.95f, Ui.Booth);
            var npc = npcImg.transform;
            // 배경 그림 (검문소·비): NPC 영역을 꽉 채우고 넘치는 부분은 잘라 냄. 아래쪽 글자 칸은 어둡게 깔아 읽히게 함
            _boothBg = Ui.Art("bg_checkpoint_rain");
            if (_boothBg != null)
            {
                npcImg.gameObject.AddComponent<RectMask2D>();
                var bg = Ui.Picture(npc, "Backdrop", 0, 0, 1, 1, _boothBg);
                var env = bg.gameObject.AddComponent<AspectRatioFitter>();
                env.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                env.aspectRatio = _boothBg.rect.width / _boothBg.rect.height;
                bg.color = new Color(0.85f, 0.85f, 0.85f);
                Ui.Box(npc, "Shade", 0, 0, 1, 0.215f, new Color(0f, 0f, 0f, 0.6f)).raycastTarget = false;
                Ui.Box(npc, "ShadeTop", 0, 0.95f, 1, 1, new Color(0f, 0f, 0f, 0.5f)).raycastTarget = false;
            }
            Ui.Label(npc, "Zone", 0.03f, 0.955f, 0.97f, 0.995f, "NPC 영역 — 방문자 얼굴 · 대화", 20, Soon);
            // 초상 (C050, 대화로그 PDF 2쪽 수정안 · 3차 회의 "캐릭터는 화면을 꽉 채우게"): NPC 영역 폭을 꽉 채우고
            // 머리 위를 칸 위에 맞춤. 3:4 그림이 칸보다 길어 가슴 아래는 잘림. 칸 아래 끝은 대화창 바로 위
            _boothCell = MakeCell(npc, 0.02f, 0.215f, 0.98f, 0.95f, Source.Booth, Field.Photo, null, _boothBg != null ? Color.clear : Ui.Booth * 1.2f, Ui.Light, out _);
            var faceArea = Ui.Rect(_boothCell.Bg.transform, "FaceArea", 0, 0, 1, 1, 4);
            _boothFace = Ui.Rect(faceArea, "Face", 0, 0, 1, 1);
            // 판정 결과 띠: 초상 바깥 아래, 판정 뒤 첫마디 칸 위에 뜸 (첫마디는 대화 로그에 남음). 그림 비율 유지, 글자는 그림의 글줄 칸 2개에 씀
            _bandOk = Ui.Art("band_ok");
            _bandBad = Ui.Art("band_bad");
            if (_bandOk != null)
            {
                var band = Ui.Fit(npc, "VerdictBandArea", 0.02f, 0.01f, 0.98f, 0.215f, _bandOk.rect.width / _bandOk.rect.height);
                _verdictBand = band.gameObject.AddComponent<Image>();
                _verdictBand.sprite = _bandOk;
                _verdictBand.raycastTarget = false;
                _verdictText = Ui.Label(band, "Text", 0.25f, 0.51f, 0.92f, 0.7f, "", 22, Color.white, TextAnchor.MiddleLeft);
                _verdictWhy = Ui.Label(band, "Why", 0.25f, 0.25f, 0.92f, 0.44f, "", 20, Color.white, TextAnchor.MiddleLeft);
                Ui.Shrink(_verdictText, 12); Ui.Shrink(_verdictWhy, 12);
            }
            else
            {
                _verdictBand = Ui.Box(npc, "VerdictBand", 0.02f, 0.015f, 0.98f, 0.165f, Ui.PassGreen);
                _verdictBand.raycastTarget = false;
                _verdictText = Ui.Label(_verdictBand.transform, "Text", 0.02f, 0, 0.98f, 1, "", 22, Color.white, TextAnchor.MiddleCenter);
            }
            _verdictBand.gameObject.SetActive(false);

            // 대화창: NPC 영역 맨 아래 (PDF 2쪽 수정안). 하단 버튼 줄 없음
            _who = Ui.Label(npc, "Who", 0.03f, 0.17f, 0.97f, 0.21f, "대화창 — 방문자의 첫마디", 20, new Color(0.7f, 0.7f, 0.7f));
            var bubble = Ui.Box(npc, "Bubble", 0.02f, 0.015f, 0.98f, 0.165f, Ui.Light).transform;
            _bubble = bubble.gameObject;
            _speech = Ui.Shrink(Ui.Label(bubble, "Text", 0, 0, 1, 1, "", 30, Ui.Ink, TextAnchor.MiddleLeft, 20), 18);

            // 대화 로그 버튼: 좌측 상단, 초상 위 (PDF 2·3쪽). 단축키 [Tab] 유지
            var logBtn = Ui.Button(npc, "LogOpen", 0.03f, 0.885f, 0.37f, 0.94f, "대화 로그 [Tab]", new Color(0.06f, 0.06f, 0.07f, 0.85f), Color.white, 22, () => { SetLog(!LogOpen); Play(_tick); });
            // 검사 모드 안내 줄: 하단 버튼 줄이 빠져 초상 아래쪽에 겹쳐 띄움 (기획 원문에 자리 없음 — 가정)
            _inspectLine = Ui.Label(npc, "Inspect", 0.04f, 0.22f, 0.96f, 0.285f, "", 24, Ui.Pick, TextAnchor.MiddleLeft);
            _inspectLine.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
            _inspectLine.raycastTarget = false;
            (_bandOk != null ? _verdictBand.transform.parent : _verdictBand.transform).SetAsLastSibling(); // 띠는 대화창 위에 뜸
            BuildLog(npc);
            logBtn.transform.SetAsLastSibling(); // 로그 덮개 위에도 버튼이 보임. 다시 누르면 닫힘 (PDF 4쪽 수정안 그림에 닫기 버튼 없음 — 가정)

            // ② 서류 검토 영역 (우측 상단): 방문자가 낸 작은 서류가 위에서 내려옴 → 클릭하면 확대 (닫기 · Esc)
            //    그날 필요한 서류 중 방문자가 낸 것만 칸이 생김. 안 낸 서류는 칸·글자 없음
            var deskImg = Ui.Box(root, "DocArea", 0.405f, 0.455f, 0.8f, 0.95f, Ui.Desk);
            deskImg.gameObject.AddComponent<RectMask2D>(); // 위에서 내려오는 서류가 영역 밖에서는 안 보이게
            _desk = deskImg.rectTransform;
            var desk = _desk.transform;
            Ui.Label(desk, "Zone", 0.03f, 0.9f, 0.97f, 0.99f, "서류 검토 — 서류를 클릭하면 크게 봅니다", 20, Soon);
            _tray = Ui.Rect(desk, "Tray", 0, 0, 1, 0.9f);
            _docView = (RectTransform)Ui.Box(desk, "OpenDoc", 0.02f, 0.02f, 0.98f, 0.98f, Ui.Paper).transform;
            // 확대된 서류를 다시 클릭 → 작은 서류 자리로 축소 (칸·닫기 버튼 클릭은 각자 처리)
            var reclick = _docView.gameObject.AddComponent<Button>();
            reclick.transition = Selectable.Transition.None;
            reclick.onClick.AddListener(ShrinkDoc);
            _docContent = Ui.Rect(_docView, "Content", 0, 0, 1, 1);
            Ui.Button(_docView, "Close", 0.8f, 0.9f, 0.98f, 0.985f, "닫기 [Esc]", new Color(0.3f, 0.27f, 0.22f), Color.white, 20, () => { CloseDoc(); Play(_tick); });
            _docView.gameObject.SetActive(false);

            // ③ 단말기 / DB 영역 (우측 하단): 주민등록 DB · 규칙 및 매뉴얼 (탭)
            var term = Ui.Box(root, "Terminal", 0.405f, 0, 0.8f, 0.45f, Ui.Terminal).transform;
            string[] termNames = { "주민등록 DB", "규칙 및 매뉴얼" };
            for (int i = 0; i < termNames.Length; i++)
            {
                int k = i;
                _termTabs.Add(Ui.Button(term, "TermTab" + i, 0.02f + i * 0.33f, 0.88f, 0.34f + i * 0.33f, 0.98f, termNames[i], Ui.Terminal * 2.2f, Ui.TermText, 22, () => { _termTab = k; RebuildTerminal(); Play(_tick); }));
            }
            Ui.Label(term, "Zone", 0.69f, 0.88f, 0.98f, 0.98f, "단말기", 20, Ui.TermText * 0.6f, TextAnchor.MiddleRight);
            _termContent = Ui.Rect(term, "Content", 0, 0, 1, 0.87f, 8);

            // ④ 판정 버튼 (우측): 최종 처분 4종 + 검사 모드 + 다음
            var verdict = Ui.Box(root, "Verdict", 0.805f, 0, 1, 0.95f, Ui.Booth).transform;
            Ui.Label(verdict, "Zone", 0.06f, 0.955f, 0.94f, 0.995f, "최종 처분", 20, Soon);
            // 통과·거부: 받은 금속판 그림 4상태 (기본·마우스 올림·클릭·비활성). 글자는 그림 위에 게임 글꼴로
            var passArt = Ui.ArtSet("btn_pass_normal", "btn_pass_hover", "btn_pass_pressed", "btn_pass_disabled");
            var denyArt = Ui.ArtSet("btn_deny_normal", "btn_deny_hover", "btn_deny_pressed", "btn_deny_disabled");
            if (passArt[0] != null && denyArt[0] != null)
            {
                float a = passArt[0].rect.width / passArt[0].rect.height;
                _passBtn = Ui.SpriteButton(Ui.Fit(verdict, "PassArea", 0.04f, 0.81f, 0.96f, 0.95f, a), "Pass", 0, 0, 1, 1, "통과", passArt, Color.white, 44, () => Decide(Verdict.Pass));
                _denyBtn = Ui.SpriteButton(Ui.Fit(verdict, "DenyArea", 0.04f, 0.66f, 0.96f, 0.8f, a), "Deny", 0, 0, 1, 1, "거부", denyArt, Color.white, 44, () => Decide(Verdict.Deny));
                foreach (var b in new[] { _passBtn, _denyBtn }) b.GetComponentInChildren<Text>().gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.7f);
            }
            else
            {
                _passBtn = Ui.Button(verdict, "Pass", 0.06f, 0.81f, 0.94f, 0.95f, "통과", Ui.PassGreen, Color.white, 40, () => Decide(Verdict.Pass));
                _denyBtn = Ui.Button(verdict, "Deny", 0.06f, 0.66f, 0.94f, 0.8f, "거부", Ui.DenyRed, Color.white, 40, () => Decide(Verdict.Deny));
            }
            // 순서 (위→아래): 통과·거부·제거·신고 (메인 기획 소감 261123)
            // 제거: 1일차부터 (얼굴에 이상한 부분 → 제거, 1주차 스토리 261122). 신고: 5일차부터 보임 (그 전엔 숨김, StartDay)
            _killBtn = Ui.Button(verdict, "Kill", 0.06f, 0.54f, 0.94f, 0.65f, "제거", new Color(0.35f, 0.05f, 0.05f), Color.white, 36, () => Decide(Verdict.Kill));
            _reportBtn = Ui.Button(verdict, "Report", 0.06f, 0.42f, 0.94f, 0.53f, "신고", Ui.Disabled, Soon, 24, null);
            _reportBtn.interactable = false; // 신고 판정은 5일차 규칙이 오면 연결
            _reportBtn.gameObject.SetActive(false);
            // 검사 모드: 끔/켬 × 기본·마우스 올림·클릭 그림. 글자는 그림 가운데 회색 글줄 칸에
            _inspectArtOff = Ui.ArtSet("inspect_off_normal", "inspect_off_hover", "inspect_off_pressed");
            _inspectArtOn = Ui.ArtSet("inspect_on_normal", "inspect_on_hover", "inspect_on_pressed");
            if (_inspectArtOff[0] == null || _inspectArtOn[0] == null) _inspectArtOff = _inspectArtOn = null;
            if (_inspectArtOn != null)
            {
                var area = Ui.Fit(verdict, "InspectArea", 0.04f, 0.225f, 0.96f, 0.365f, _inspectArtOff[0].rect.width / _inspectArtOff[0].rect.height);
                _inspectBtn = Ui.SpriteButton(area, "Inspect", 0, 0, 1, 1, "", _inspectArtOff, Ui.Ink, 20, ToggleInspect);
                _inspectBtnText = _inspectBtn.GetComponentInChildren<Text>();
                var rt = _inspectBtnText.rectTransform;
                rt.anchorMin = new Vector2(0.27f, 0.32f); rt.anchorMax = new Vector2(0.71f, 0.66f); rt.offsetMin = rt.offsetMax = Vector2.zero;
                _inspectBtnText.color = new Color(0.06f, 0.06f, 0.06f);
                _inspectBtnText.fontSize = 22; _inspectBtnText.fontStyle = FontStyle.Bold; Ui.Shrink(_inspectBtnText, 14);
                // 클릭 그림은 글줄 칸이 18px(그림 높이 242 중) 아래로 내려감 → 글자도 같은 만큼 내림
                var follow = _inspectBtn.gameObject.AddComponent<LabelFollowSprite>();
                follow.Target = _inspectBtn.image; follow.Label = rt;
                follow.BaseMin = rt.anchorMin; follow.BaseMax = rt.anchorMax;
                follow.Moved.Add(_inspectArtOff[2]); follow.Moved.Add(_inspectArtOn[2]);
                follow.Shift = new Vector2(0f, -18f / 242f);
                Ui.Label(verdict, "InspectKey", 0.06f, 0.19f, 0.94f, 0.225f, "[Space] 키로 켜고 끄기", 18, Soon, TextAnchor.MiddleCenter);
            }
            else
            {
                _inspectBtn = Ui.Button(verdict, "Inspect", 0.06f, 0.22f, 0.94f, 0.36f, "", new Color(0.55f, 0.45f, 0.1f), Color.white, 24, ToggleInspect);
                _inspectBtnText = _inspectBtn.GetComponentInChildren<Text>();
            }
            _nextBtn = Ui.Button(verdict, "Next", 0.06f, 0.03f, 0.94f, 0.18f, "다음 방문자 ▶", new Color(0.25f, 0.35f, 0.55f), Color.white, 28, Next);
            SetInspect(false);
        }

        // 검역 소견서를 보는 날인지 (규칙에 검역 항목이 켜진 날부터 칸이 생김)
        bool CertDay => Day.Has(ErrorKind.QuarantineMissing) || Day.Has(ErrorKind.QuarantineExpired);
        // 입성 사유서를 보는 날인지 (3일차~, 1주차 스토리 261122)
        bool PermitDay => Day.Has(ErrorKind.EntryMissing) || Day.Has(ErrorKind.EntryNameMismatch) || Day.Has(ErrorKind.EntryReasonDenied) || Day.Has(ErrorKind.EntryDestMismatch);
        static bool IsDocSrc(Source s) => s == Source.TravelPass || s == Source.Quarantine || s == Source.EntryPermit;
        bool Missing(Doc d) => d switch { Doc.Pass => V.PassMissing, Doc.Cert => V.Cert == null, _ => V.Permit == null };

        // 작은 서류 칸을 만들고 위에서 아래로 내려보냄. 방문자가 낸 서류만 칸이 생김 (안 낸 서류는 없는 것으로 처리).
        void BuildSmallDocs()
        {
            if (_dropCo != null) StopCoroutine(_dropCo);
            Ui.Clear(_tray);
            _smallDocs.Clear();
            _docKinds.Clear();
            if (!Missing(Doc.Pass)) _docKinds.Add(Doc.Pass);
            if (CertDay && !Missing(Doc.Cert)) _docKinds.Add(Doc.Cert);
            if (PermitDay && !Missing(Doc.Permit)) _docKinds.Add(Doc.Permit);
            for (int i = 0; i < _docKinds.Count; i++) _smallDocs.Add(MakeSmallDoc(i, _docKinds[i]));
            _dropCo = StartCoroutine(DropDocs(new List<RectTransform>(_smallDocs)));
        }

        RectTransform MakeSmallDoc(int i, Doc kind)
        {
            int k = i;
            var passArt = Ui.Art("pass_card");
            if (kind == Doc.Pass && passArt != null)
            {
                // 임시 통행증: 받은 카드 그림 (가로 카드). 제목은 그림 위 초록 띠 글줄 칸에
                var area = Ui.Rect(_tray, "SmallDoc0", 0.03f, 0.3f, 0.47f, 0.9f);
                var fit = Ui.Fit(area, "Card", 0, 0, 1, 1, PassAspect);
                var card = Ui.Button(fit, "Button", 0, 0, 1, 1, "", Color.white, Ui.Ink, 18, () => OpenDoc(k));
                card.image.sprite = passArt;
                Destroy(card.GetComponentInChildren<Text>().gameObject);
                Ui.Label(card.transform, "Title", 0.18f, 0.838f, 0.82f, 0.914f, DocName(Doc.Pass), 16, Ui.Ink, TextAnchor.MiddleCenter);
                Ui.Label(card.transform, "Hint", 0.08f, 0.02f, 0.5f, 0.17f, "클릭", 16, Ui.Ink * 2.2f, TextAnchor.MiddleLeft);
                return area;
            }
            // 통행증 칸은 왼쪽 큰 자리, 나머지 서류는 오른쪽에 나란히 (3장이면 폭을 줄임)
            float w = passArt != null && i > 0 ? (_docKinds.Count > 2 ? 0.2f : 0.22f) : 0.22f;
            float gap = _docKinds.Count > 2 ? 0.235f : 0.27f;
            float x0 = i == 0 ? (passArt != null ? 0.14f : 0.05f) : passArt != null ? 0.52f + (i - 1) * gap : 0.05f + i * 0.27f;
            var b = Ui.Button(_tray, "SmallDoc" + i, x0, 0.08f, x0 + w, 0.86f, "", Ui.Paper, Ui.Ink, 18, () => OpenDoc(k));
            var t = b.transform;
            Ui.Label(t, "Title", 0.06f, 0.8f, 0.94f, 0.97f, DocName(kind), 20, Ui.Ink, TextAnchor.MiddleCenter);
            var line = Ui.Solid(Ui.Paper * 0.75f);
            if (kind == Doc.Pass) // 통행증: 사진 자리 + 글줄 + 도장
            {
                Ui.Box(t, "Photo", 0.1f, 0.5f, 0.42f, 0.76f, line);
                for (int r = 0; r < 4; r++) Ui.Box(t, "Line" + r, 0.48f, 0.72f - r * 0.065f, 0.9f, 0.745f - r * 0.065f, line);
                for (int r = 0; r < 2; r++) Ui.Box(t, "Line" + (r + 4), 0.1f, 0.4f - r * 0.065f, 0.9f, 0.425f - r * 0.065f, line);
            }
            else if (kind == Doc.Cert) // 검역 소견서: 글줄 + 검사 칸 3개
            {
                for (int r = 0; r < 3; r++) Ui.Box(t, "Line" + r, 0.1f, 0.72f - r * 0.065f, 0.9f, 0.745f - r * 0.065f, line);
                for (int r = 0; r < 3; r++) Ui.Box(t, "Box" + r, 0.1f, 0.42f - r * 0.1f, 0.9f, 0.5f - r * 0.1f, line);
            }
            else // 입성 사유서: 글줄 3개 (이름 · 사유 · 목적지)
            {
                for (int r = 0; r < 3; r++) Ui.Box(t, "Line" + r, 0.1f, 0.68f - r * 0.12f, 0.9f, 0.72f - r * 0.12f, line);
            }
            if (kind != Doc.Permit) Ui.Circle(t, "Stamp", 0.56f, 0.05f, 0.9f, 0.2f, new Color(0.7f, 0.12f, 0.1f, 0.75f));
            Ui.Label(t, "Hint", 0.04f, 0.02f, 0.55f, 0.2f, "클릭", 16, Ui.Ink * 2.2f, TextAnchor.MiddleLeft);
            return b.image.rectTransform;
        }

        IEnumerator DropDocs(List<RectTransform> docs)
        {
            foreach (var d in docs) d.anchoredPosition = new Vector2(0, DropFrom);
            float total = DropTime + DropStagger * Mathf.Max(0, docs.Count - 1), t = 0f;
            while (t < total)
            {
                t += Time.deltaTime;
                for (int i = 0; i < docs.Count; i++)
                {
                    float k = Mathf.Clamp01((t - i * DropStagger) / DropTime);
                    float e = 1f - Mathf.Pow(1f - k, 3f); // 빠르게 떨어지다 책상에서 멈춤
                    docs[i].anchoredPosition = new Vector2(0, Mathf.Lerp(DropFrom, 0f, e));
                }
                yield return null;
            }
            foreach (var d in docs) d.anchoredPosition = Vector2.zero;
            Play(_tick);
            _dropCo = null;
        }

        // 작은 서류 클릭 → 서류 검토 영역 가득 확대 (작은 서류 자리에서 커지는 움직임)
        void OpenDoc(int i)
        {
            if (_overlay != null || i < 0 || i >= _smallDocs.Count) return;
            _tab = i;
            _docView.gameObject.SetActive(true);
            RebuildDoc();
            if (_zoomCo != null) StopCoroutine(_zoomCo);
            RestoreDocHome();
            _zoomCo = StartCoroutine(ZoomFrom(_smallDocs[i]));
            Play(_tick);
        }

        // 확대된 서류 다시 클릭: 작은 서류 자리로 줄어드는 움직임 뒤 닫힘 (커질 때의 반대)
        void ShrinkDoc()
        {
            if (_overlay != null || !DocOpen || _docHome.HasValue) return;
            if (_tab < 0 || _tab >= _smallDocs.Count) { CloseDoc(); Play(_tick); return; }
            if (_zoomCo != null) StopCoroutine(_zoomCo);
            _zoomCo = StartCoroutine(ShrinkTo(_smallDocs[_tab]));
            Play(_tick);
        }

        IEnumerator ShrinkTo(RectTransform small)
        {
            Vector3 home = _docView.localPosition;
            _docHome = home;
            Vector3 to = _desk.InverseTransformPoint(small.TransformPoint(small.rect.center));
            Vector3 s1 = new(small.rect.width / Mathf.Max(1f, _docView.rect.width), small.rect.height / Mathf.Max(1f, _docView.rect.height), 1f);
            Vector3 s0 = _docView.localScale;
            float t = 0f;
            while (t < ZoomTime)
            {
                t += Time.deltaTime;
                float e = Mathf.Pow(Mathf.Clamp01(t / ZoomTime), 2f);
                _docView.localPosition = Vector3.Lerp(home, to, e);
                _docView.localScale = Vector3.Lerp(s0, s1, e);
                yield return null;
            }
            _zoomCo = null;
            CloseDoc();
        }

        void RestoreDocHome()
        {
            if (!_docHome.HasValue || _docView == null) return;
            _docView.localPosition = _docHome.Value;
            _docView.localScale = Vector3.one;
            _docHome = null;
        }

        void CloseDoc()
        {
            if (_zoomCo != null) { StopCoroutine(_zoomCo); _zoomCo = null; }
            RestoreDocHome();
            _tab = -1;
            if (_pickA != null && IsDocSrc(_pickA.Src)) SetInspect(_inspect);
            if (_docView != null)
            {
                _docView.localScale = Vector3.one;
                _docView.gameObject.SetActive(false);
            }
            RebuildDoc();
        }

        IEnumerator ZoomFrom(RectTransform small)
        {
            Vector3 home = _docView.localPosition;
            _docHome = home;
            Vector3 from = _desk.InverseTransformPoint(small.TransformPoint(small.rect.center));
            Vector3 s0 = new(small.rect.width / Mathf.Max(1f, _docView.rect.width), small.rect.height / Mathf.Max(1f, _docView.rect.height), 1f);
            float t = 0f;
            while (t < ZoomTime)
            {
                t += Time.deltaTime;
                float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / ZoomTime), 2f);
                _docView.localPosition = Vector3.Lerp(from, home, e);
                _docView.localScale = Vector3.Lerp(s0, Vector3.one, e);
                yield return null;
            }
            _docView.localPosition = home;
            _docView.localScale = Vector3.one;
            _docHome = null;
            _zoomCo = null;
        }

        // 임시통행증 글줄 5칸. 1일차(DB 없음): 생년월일(나이) 칸 — 말한 나이 대조. 2일차~: 범죄기록 칸 (1주차 스토리 261122)
        (Field f, string v)[] PassRows(TravelPass p) => DbDay
            ? new[] { (Field.Name, p.Name), (Field.ResidentId, p.ResidentId), (Field.Address, p.Address), (Field.CriminalRecord, p.CriminalRecord ?? "없음"), (Field.IssueDate, Fmt.Date(p.IssueDate)) }
            : new[] { (Field.Name, p.Name), (Field.ResidentId, p.ResidentId), (Field.BirthDate, $"{Fmt.Date(p.BirthDate)} ({Portraits.Age(p.BirthDate)}세)"), (Field.Address, p.Address), (Field.IssueDate, Fmt.Date(p.IssueDate)) };

        void RebuildDoc()
        {
            _cells.RemoveAll(c => IsDocSrc(c.Src));
            Ui.Clear(_docContent);
            if (_tab < 0 || _tab >= _docKinds.Count) return;
            var t = _docContent.transform;
            var kind = _docKinds[_tab];
            var passArt = Ui.Art("pass_card");
            bool passCard = kind == Doc.Pass && passArt != null;
            _docView.GetComponent<Image>().color = passCard ? Color.clear : Ui.Paper;
            if (passCard)
            {
                // 임시 통행증 카드 그림 위에 칸을 그림 기준 비율로 얹음 (사진 점선 칸 · 글줄 5칸 · 도장 자리)
                var p = V.Pass;
                var card = Ui.Fit(t, "Card", 0, 0, 1, 0.9f, PassAspect);
                Ui.Picture(card, "Art", 0, 0, 1, 1, passArt);
                Ui.Shrink(Ui.Label(card, "Title", 0.18f, 0.838f, 0.82f, 0.914f, "임시 통행증 — 1차 선별소 발급", 22, Ui.Ink, TextAnchor.MiddleCenter), 12);
                var photo = MakeCell(card, 0.083f, 0.219f, 0.379f, 0.754f, Source.TravelPass, Field.Photo, null, new Color(1f, 1f, 1f, 0f), Ui.Ink, out _);
                FaceView.Draw(Ui.Rect(photo.Bg.transform, "Face", 0, 0, 1, 1, 9), p.Photo, new Color(0.8f, 0.8f, 0.78f));
                // 글줄 칸 가운데 (원본 1024줄 기준 위에서부터): 281 · 370 · 459 · 548 · 640
                float[] rowY = { 281, 370, 459, 548, 640 };
                var rows = PassRows(p);
                for (int r = 0; r < rows.Length; r++)
                    MakeCell(card, 0.405f, 1f - (rowY[r] + 40f) / 1024f, 0.925f, 1f - (rowY[r] - 40f) / 1024f, Source.TravelPass, rows[r].f, rows[r].v, Ui.Paper, Ui.Ink, out _, DocCellArt);
                // 도장 자리 (그림 오른쪽 아래): 도장 글자는 코드로 씀 (위조 도장 판정에 글자가 필요)
                var stamp = MakeCell(card, 0.72f, 0.08f, 0.93f, 0.34f, Source.TravelPass, Field.Stamp, p.Stamp, new Color(1f, 1f, 1f, 0f), new Color(0.7f, 0.12f, 0.1f), out var stv);
                var ring = Ui.Circle(stamp.Bg.transform, "Ring", 0.12f, 0.02f, 0.88f, 0.98f, new Color(0.7f, 0.12f, 0.1f, 0.16f));
                ring.raycastTarget = false;
                ring.transform.SetAsFirstSibling();
                Destroy(stamp.Bg.transform.Find("Name").gameObject);
                var sr = stv.rectTransform;
                sr.anchorMin = new Vector2(0.14f, 0.1f); sr.anchorMax = new Vector2(0.86f, 0.9f);
                stv.alignment = TextAnchor.MiddleCenter;
                stv.fontSize = 24; Ui.Shrink(stv, 12);
            }
            else if (kind == Doc.Pass)
            {
                var p = V.Pass;
                Ui.Label(t, "Title", 0.03f, 0.88f, 0.97f, 0.99f, "임시 통행증 — 1차 선별소 발급", 24, Ui.Ink);
                var photo = MakeCell(t, 0.03f, 0.4f, 0.24f, 0.86f, Source.TravelPass, Field.Photo, null, Ui.Solid(Ui.Paper * 0.9f), Ui.Ink, out _);
                FaceView.Draw(Ui.Rect(photo.Bg.transform, "Face", 0, 0, 1, 1, 4), p.Photo, new Color(0.8f, 0.8f, 0.78f));
                var rows = PassRows(p);
                for (int r = 0; r < 4; r++) DocRow(t, 0.27f, 0.86f - r * 0.12f, Source.TravelPass, rows[r].f, rows[r].v);
                DocRow(t, 0.03f, 0.36f, Source.TravelPass, rows[4].f, rows[4].v);
                MakeCell(t, 0.03f, 0.04f, 0.6f, 0.22f, Source.TravelPass, Field.Stamp, p.Stamp, Ui.Solid(Ui.Paper * 0.95f), new Color(0.7f, 0.12f, 0.1f), out var st);
                st.fontSize = 28;
            }
            else if (kind == Doc.Permit)
            {
                // 입성 사유서 (이름 · 사유 · 목적지). 목적지는 방문자가 말한 목적지와 대조 (대화 로그)
                var e = V.Permit;
                Ui.Label(t, "Title", 0.03f, 0.88f, 0.97f, 0.99f, "입성 사유서", 24, Ui.Ink);
                DocRowW(t, 0.03f, 0.80f, 0.97f, Source.EntryPermit, Field.Name, e.Name);
                DocRowW(t, 0.03f, 0.64f, 0.97f, Source.EntryPermit, Field.Reason, e.Reason);
                DocRowW(t, 0.03f, 0.48f, 0.97f, Source.EntryPermit, Field.Destination, e.Destination);
            }
            else
            {
                var c = V.Cert;
                Ui.Label(t, "Title", 0.03f, 0.88f, 0.97f, 0.99f, "검역 소견서", 24, Ui.Ink);
                // 간이 · 정밀 · 심층 검사 칸 자리 (판독 기준 미정 → 준비 중)
                string[] levels = { "간이: 혈압 · 맥박 · 체온", "정밀: 채혈(색·점도) · 폐활량(폐 잡음)", "심층: 간이 PCR · 흉부 X-ray" };
                for (int i = 0; i < levels.Length; i++)
                {
                    float y1 = 0.86f - i * 0.28f;
                    var box = Ui.Box(t, "Level" + i, 0.56f, y1 - 0.26f, 0.97f, y1, Ui.Solid(Ui.Paper * 0.88f)).transform;
                    Ui.Label(box, "Name", 0.04f, 0.45f, 0.96f, 0.98f, levels[i], 18, Ui.Ink, TextAnchor.MiddleLeft);
                    Ui.Label(box, "Soon", 0.04f, 0.02f, 0.96f, 0.45f, "(준비 중)", 18, Ui.Ink * 2.5f, TextAnchor.MiddleLeft);
                }
                DocRowW(t, 0.03f, 0.86f, 0.54f, Source.Quarantine, Field.Name, c.Name);
                DocRowW(t, 0.03f, 0.73f, 0.54f, Source.Quarantine, Field.TestDate, Fmt.Date(c.TestDate));
                DocRowW(t, 0.03f, 0.60f, 0.54f, Source.Quarantine, Field.TestResult, c.Result);
                MakeCell(t, 0.03f, 0.2f, 0.54f, 0.38f, Source.Quarantine, Field.Stamp, c.Stamp, Ui.Solid(Ui.Paper * 0.95f), new Color(0.7f, 0.12f, 0.1f), out var st);
                st.fontSize = 26;
                Ui.Label(t, "Hint", 0.03f, 0.01f, 0.54f, 0.17f, $"오늘 {Fmt.Date(Day.Today)}\n검사일 {Rules.QuarantineValidDays}일 이내만 유효", 18, Ui.Ink * 1.6f);
            }
        }

        void DocRow(Transform t, float x0, float yTop, Source src, Field f, string value) => DocRowW(t, x0, yTop, 0.97f, src, f, value);

        void DocRowW(Transform t, float x0, float yTop, float x1, Source src, Field f, string value) =>
            MakeCell(t, x0, yTop - 0.11f, x1, yTop, src, f, value, Ui.Solid(Ui.Paper * 0.95f), Ui.Ink, out _, DocCellArt);

        void RebuildTerminal()
        {
            for (int i = 0; i < _termTabs.Count; i++) _termTabs[i].image.color = i == _termTab ? Ui.Terminal * 4f : Ui.Terminal * 1.6f;
            _cells.RemoveAll(c => c.Src == Source.Database);
            Ui.Clear(_termContent);
            var t = _termContent.transform;
            if (!DbDay) _termTab = 1; // 1일차: 주민등록 DB 없음
            if (_termTab == 1) { BuildManual(t); return; }
            var r = Record;
            var dbArt = Ui.Art(r == null ? "db_none" : "db_screen");
            if (dbArt != null)
            {
                BuildDbScreen(t, r, dbArt);
                return;
            }
            Ui.Label(t, "Search", 0.02f, 0.88f, 0.98f, 0.99f, $"이름 검색 > {(V.PassMissing ? "" : V.Pass.Name)}_", 22, Ui.TermText * 0.85f);
            if (r == null)
            {
                Ui.Label(t, "None", 0.05f, 0.35f, 0.95f, 0.75f, "검색 결과 없음\n등록되지 않은 이름입니다.", 28, new Color(1f, 0.5f, 0.45f), TextAnchor.MiddleCenter);
            }
            else
            {
                var bg = Ui.Terminal * 2.2f;
                var photo = MakeCell(t, 0.02f, 0.36f, 0.25f, 0.86f, Source.Database, Field.Photo, null, bg, Ui.TermText, out _);
                FaceView.Draw(Ui.Rect(photo.Bg.transform, "Face", 0, 0, 1, 1, 4), r.Face, new Color(0.25f, 0.32f, 0.27f));
                TermRow(t, 0.27f, 0.86f, Field.Name, r.Name);
                TermRow(t, 0.27f, 0.74f, Field.ResidentId, r.ResidentId);
                TermRow(t, 0.27f, 0.62f, Field.BirthDate, Fmt.Date(r.BirthDate));
                TermRow(t, 0.27f, 0.50f, Field.Address, r.Address);
                bool crimeOn = Day.Has(ErrorKind.CriminalRecord);
                MakeCell(t, 0.02f, 0.16f, 0.98f, 0.32f, Source.Database, Field.CriminalRecord,
                    crimeOn ? r.CriminalRecord ?? "없음" : "(권한 없음 — 아직 복구되지 않은 서버)", bg,
                    r.CriminalRecord != null && crimeOn ? new Color(1f, 0.5f, 0.45f) : Ui.TermText, out _);
            }
            Ui.Label(t, "Foot", 0.02f, 0.01f, 0.98f, 0.13f, "※ 멸망 전 서버 일부만 복구됨 — 사진은 등록 당시 모습", 18, Ui.TermText * 0.6f);
        }

        // 주민등록 DB 화면 그림 (1598×984) 위에 칸을 그림 기준 비율로 얹음: 검색 칸 · 사진 칸 · 글줄 4칸 · 범죄 이력 칸
        void BuildDbScreen(Transform t, Resident r, Sprite screenArt)
        {
            var scr = Ui.Fit(t, "Screen", 0, 0, 1, 1, DbAspect);
            Ui.Picture(scr, "Art", 0, 0, 1, 1, screenArt);
            Ui.Label(scr, "Search", 0.12f, 0.814f, 0.95f, 0.922f, $"이름 검색 > {(V.PassMissing ? "" : V.Pass.Name)}_", 22, Ui.TermText);
            if (r == null)
            {
                Ui.Label(scr, "None", 0.05f, 0.3f, 0.95f, 0.7f, "검색 결과 없음\n등록되지 않은 이름입니다.", 28, new Color(1f, 0.5f, 0.45f), TextAnchor.MiddleCenter);
                return;
            }
            // 사진 칸: 그림의 모서리 꺾쇠 안쪽
            var photo = MakeCell(scr, 0.066f, 0.35f, 0.266f, 0.761f, Source.Database, Field.Photo, null, new Color(1f, 1f, 1f, 0f), Ui.TermText, out _);
            FaceView.Draw(Ui.Rect(photo.Bg.transform, "Face", 0, 0, 1, 1, 6), r.Face, new Color(0.25f, 0.32f, 0.27f));
            // 글줄 칸 가운데 (원본 984줄 기준 위에서부터): 278 · 382 · 486 · 590
            float[] rowY = { 278, 382, 486, 590 };
            (Field f, string v)[] rows = { (Field.Name, r.Name), (Field.ResidentId, r.ResidentId), (Field.BirthDate, Fmt.Date(r.BirthDate)), (Field.Address, r.Address) };
            for (int i = 0; i < rows.Length; i++)
                MakeCell(scr, 0.297f, 1f - (rowY[i] + 46f) / 984f, 0.955f, 1f - (rowY[i] - 46f) / 984f, Source.Database, rows[i].f, rows[i].v, Ui.Terminal * 2.2f, Ui.TermText, out _, DbCellArt);
            // 범죄 이력 칸: 없음(체크) · 기록 있음(경고) · 권한 없음(자물쇠) 그림
            bool crimeOn = Day.Has(ErrorKind.CriminalRecord);
            bool hasCrime = r.CriminalRecord != null && crimeOn;
            var crimeArt = Ui.Art(!crimeOn ? "crime_locked" : hasCrime ? "crime_record" : "crime_none", 20);
            var crime = MakeCell(scr, 0.041f, 0.148f, 0.959f, 0.299f, Source.Database, Field.CriminalRecord,
                crimeOn ? r.CriminalRecord ?? "없음" : "(권한 없음 — 아직 복구되지 않은 서버)", Ui.Terminal * 2.2f,
                hasCrime ? new Color(1f, 0.5f, 0.45f) : Ui.TermText, out _);
            if (crimeArt != null)
            {
                Ui.Sliced(crime.Bg, crimeArt);
                crime.Normal = Color.white;
                var nr = crime.Bg.transform.Find("Name").GetComponent<RectTransform>();
                nr.anchorMin = new Vector2(0.08f, 0f); nr.anchorMax = new Vector2(0.26f, 1f);
                var vr = crime.Bg.transform.Find("Value").GetComponent<RectTransform>();
                vr.anchorMin = new Vector2(0.27f, 0f);
            }
            Ui.Label(scr, "Foot", 0.04f, 0.05f, 0.96f, 0.14f, "※ 멸망 전 서버 일부만 복구됨 — 사진은 등록 당시 모습", 16, Ui.TermText * 0.6f);
        }

        // 규칙 및 매뉴얼: 오늘의 규칙 · 필수 제출 서류 목록은 지금 규칙에서 채움. 위조 판별 예시·검역 판독 기준은 준비 중.
        void BuildManual(Transform t)
        {
            var docs = "임시 통행증" + (CertDay ? " · 검역 소견서" : "") + (PermitDay ? " · 입성 사유서" : "");
            var sb = new StringBuilder();
            sb.AppendLine("■ 오늘의 규칙: " + Day.RuleShort);
            sb.AppendLine("■ 필수 제출 서류: " + docs);
            // 확인 항목 문구는 1주차 스토리 261122 "일차별 확인 항목과 판정" 표에서 옮김
            if (Day.Has(ErrorKind.OutsiderAppearance)) sb.AppendLine("■ 얼굴에 이상한 부분이 있는가? → 제거");
            if (Day.Has(ErrorKind.PassDateWrong)) sb.AppendLine($"■ 임시통행증은 받은 날만 입성 가능 (오늘 {Fmt.Date(Day.Today)})");
            if (Day.Has(ErrorKind.AgeMismatch)) sb.AppendLine("■ 나이를 물어보고, 임시통행증의 나이와 일치하는가?");
            if (Day.Has(ErrorKind.SpokenNameMismatch)) sb.AppendLine("■ 말한 이름이 임시통행증의 이름과 일치하는가?");
            if (Day.Has(ErrorKind.PhotoMismatch)) sb.AppendLine("■ 실제 얼굴이 임시통행증의 사진과 같은가?");
            if (Day.Has(ErrorKind.NotSeoul)) sb.AppendLine("■ 서울사람인가? (1일차엔 서울사람만 받을 수 있음)");
            if (DbDay) sb.AppendLine("■ DB와 대조했을때 문제가 없는가? (주민번호, 이름, 주소)");
            if (Day.Has(ErrorKind.EntryReasonDenied)) sb.AppendLine("■ 입성사유 허용 : 공격적이지 않으면 허용 (예: 가족 합류, 의료, 배정 근무)");
            if (Day.Has(ErrorKind.EntryDestMismatch)) sb.AppendLine("■ 사유서의 목적지가 방문자가 말한 목적지와 일치하는가? (대화 로그로 확인)");
            if (Day.Has(ErrorKind.ForgedStamp)) sb.AppendLine($"■ 정상 도장: 「{Rules.GenuineStamp}」");
            if (Day.Has(ErrorKind.QuarantineExpired)) sb.AppendLine($"■ 검역 소견서 검사일: 오늘 기준 {Rules.QuarantineValidDays}일 이내");
            if (Day.Has(ErrorKind.CriminalRecord)) sb.AppendLine("■ DB에 범죄 이력이 있으면 거부");
            Ui.Shrink(Ui.Label(t, "Rules", 0.03f, 0.25f, 0.97f, 0.98f, sb.ToString().TrimEnd(), 22, Ui.TermText, TextAnchor.UpperLeft), 12);
            Ui.Label(t, "Forge", 0.03f, 0.13f, 0.97f, 0.24f, "■ 위조 서류 판별 예시 (준비 중)", 20, Ui.TermText * 0.6f, TextAnchor.MiddleLeft);
            Ui.Label(t, "Quar", 0.03f, 0.02f, 0.97f, 0.13f, "■ 검역 소견서 판독 기준 (준비 중)", 20, Ui.TermText * 0.6f, TextAnchor.MiddleLeft);
        }

        void TermRow(Transform t, float x0, float yTop, Field f, string value) =>
            MakeCell(t, x0, yTop - 0.11f, 0.98f, yTop, Source.Database, f, value, Ui.Terminal * 2.2f, Ui.TermText, out _);

        // 클릭할 수 있는 칸 (이름 + 값). 검사 모드에서 고를 수 있다.
        // art: 항목 칸 그림 3상태 (있으면 이름·값 칸 나눔 선에 맞춰 글자를 놓음)
        FieldCell MakeCell(Transform parent, float x0, float y0, float x1, float y1, Source src, Field f, string value, Color bg, Color fg, out Text valueText, Sprite[] art = null)
        {
            var cell = new FieldCell { Src = src, Field = f, Normal = art != null ? Color.white : bg, Art = art };
            var b = Ui.Button(parent, f.ToString(), x0, y0, x1, y1, "", cell.Normal, fg, 20, () => OnCellClicked(cell), art != null ? 0 : 2);
            cell.Bg = b.image;
            if (art != null) Ui.Sliced(b.image, art[0]);
            var cb = b.colors; cb.normalColor = Color.white; b.colors = cb;
            Destroy(b.GetComponentInChildren<Text>().gameObject);
            valueText = null;
            if (value != null)
            {
                float split = art != null ? 0.235f : 0.3f; // 그림의 칸 나눔 선 위치
                var nameText = Ui.Label(b.transform, "Name", 0.02f, 0, split, 1, Fmt.FieldName(f), 21, Ui.Solid(Color.Lerp(fg, bg, 0.35f)), art != null ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft, 4);
                valueText = Ui.Label(b.transform, "Value", split + 0.01f, 0, 0.98f, 1, value, 26, fg, TextAnchor.MiddleLeft, 4);
                Ui.Shrink(nameText, 10); Ui.Shrink(valueText, 10);
            }
            if (src != Source.Booth) _cells.Add(cell);
            return cell;
        }

        // ───────────── 대화 로그 ─────────────
        // 대화로그 PDF 4쪽 수정안 (C051): NPC 영역을 반투명하게 어둡게 덮고, 지금 검사 중인 방문자의 대화만 말풍선으로 띄움.
        // 플레이어는 우측, 방문자는 좌측. 날짜 줄 · 판정 줄 없음. 스크롤바 없음 (휠 스크롤은 남김 — 가정).
        // 열기·닫기: 좌측 상단 "대화 로그 [Tab]" 버튼 또는 Tab. 새 방문자가 오면 닫히고 비워짐.

        // 플레이어 말: PDF 4쪽 수정안 그림 문구 그대로 (기획 대사 원문 없음 — 가정)
        const string PlayerAsk = "통행증 제출 부탁드립니다.";

        void BuildLog(Transform npc)
        {
            var panel = Ui.Box(npc, "Log", 0, 0, 1, 1, new Color(0f, 0f, 0f, 0.6f));
            _logPanel = panel.rectTransform;
            var t = panel.transform;

            var view = Ui.Box(t, "View", 0.04f, 0.215f, 0.96f, 0.87f, new Color(0, 0, 0, 0));
            view.gameObject.AddComponent<RectMask2D>();
            _logContent = Ui.Rect(view.transform, "Content", 0, 1, 1, 1);
            _logContent.pivot = new Vector2(0.5f, 1f);
            var vlg = _logContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 14;
            vlg.padding = new RectOffset(6, 6, 6, 6);
            _logContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _logScroll = view.gameObject.AddComponent<ScrollRect>();
            _logScroll.horizontal = false;
            _logScroll.viewport = view.rectTransform;
            _logScroll.content = _logContent;
            _logScroll.movementType = ScrollRect.MovementType.Clamped;
            _logScroll.scrollSensitivity = 40;

            panel.gameObject.SetActive(false);
        }

        // 말풍선 한 개: 이름 줄 + 말. right = 플레이어(우측), 아니면 방문자(좌측)
        void AddBubble(bool right, string who, string text)
        {
            var row = Ui.Rect(_logContent, right ? "Player" : "Visitor", 0, 0, 1, 1);
            var rv = row.gameObject.AddComponent<VerticalLayoutGroup>();
            rv.childControlWidth = rv.childControlHeight = true;
            rv.childForceExpandWidth = rv.childForceExpandHeight = false;
            rv.childAlignment = right ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            rv.spacing = 6;
            var align = right ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            var whoText = Ui.Label(row, "Who", 0, 0, 1, 1, who, 26, Color.white, align);
            whoText.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
            var bubble = Ui.Box(row, "Bubble", 0, 0, 1, 1, new Color(0f, 0f, 0f, 0.55f));
            bubble.raycastTarget = false;
            var bv = bubble.gameObject.AddComponent<VerticalLayoutGroup>();
            bv.childControlWidth = bv.childControlHeight = true;
            bv.childForceExpandWidth = true; bv.childForceExpandHeight = false;
            bv.padding = new RectOffset(16, 16, 10, 10);
            var be = bubble.gameObject.AddComponent<LayoutElement>();
            be.preferredWidth = 560; be.flexibleWidth = 0; // 말풍선 폭: 로그 폭의 약 80% (PDF 그림 눈대중 — 가정)
            Ui.Label(bubble.transform, "Text", 0, 0, 1, 1, text, 26, Color.white, align);
            if (LogOpen) ScrollLogToEnd();
        }

        void SetLog(bool on)
        {
            if (_logPanel == null) return;
            _logPanel.gameObject.SetActive(on);
            if (on) ScrollLogToEnd();
        }

        void ClearLog()
        {
            for (int i = _logContent.childCount - 1; i >= 0; i--) DestroyImmediate(_logContent.GetChild(i).gameObject);
        }

        void ScrollLogToEnd()
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_logContent);
            _logScroll.verticalNormalizedPosition = 0f;
        }

        // ───────────── 겹쳐 띄우는 창 ─────────────

        RectTransform OpenOverlay()
        {
            CloseOverlay();
            _overlay = (RectTransform)Ui.Box(_canvas, "Overlay", 0, 0, 1, 1, Ui.Dim).transform;
            return Frame(_overlay);
        }

        // 가운데 고정 1920×1080 판 (FitScreen이 이 판이 화면 안에 꼭 맞게 배율을 고름)
        static RectTransform Frame(Transform parent)
        {
            var f = Ui.Rect(parent, "Frame", 0.5f, 0.5f, 0.5f, 0.5f);
            f.sizeDelta = new Vector2(1920, 1080);
            return f;
        }

        void CloseOverlay()
        {
            if (_overlay != null) Destroy(_overlay.gameObject);
            _overlay = null;
        }

        static string Name(Verdict v) => v switch { Verdict.Pass => "통과", Verdict.Deny => "거부", Verdict.Kill => "제거", _ => "신고" };

        // ───────────── 임시 효과음 (코드로 만든 소리, 정식 효과음 전까지) ─────────────

        void BuildAudio()
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _stamp = Synth("Stamp", 0.18f, (t, i) => (Mathf.Sin(2 * Mathf.PI * 70 * t) * 0.9f + (UnityEngine.Random.value - 0.5f) * 0.6f) * Mathf.Exp(-t * 28));
            _buzz = Synth("Buzz", 0.28f, (t, i) => Mathf.Sign(Mathf.Sin(2 * Mathf.PI * 140 * t)) * 0.25f * (1 - t / 0.28f));
            _ding = Synth("Ding", 0.35f, (t, i) => Mathf.Sin(2 * Mathf.PI * 880 * t) * 0.35f * Mathf.Exp(-t * 9));
            _tick = Synth("Tick", 0.04f, (t, i) => (UnityEngine.Random.value - 0.5f) * 0.4f * Mathf.Exp(-t * 120));
        }

        static AudioClip Synth(string name, float seconds, Func<float, int, float> f)
        {
            const int rate = 44100;
            int n = (int)(seconds * rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = f(i / (float)rate, i);
            var clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void Play(AudioClip c, float delay = 0f)
        {
            if (c == null) return;
            if (delay <= 0f) _audio.PlayOneShot(c);
            else StartCoroutine(PlayLater(c, delay));
        }

        IEnumerator PlayLater(AudioClip c, float d) { yield return new WaitForSeconds(d); _audio.PlayOneShot(c); }

        // ───────────── 자동 확인용 스크린샷 (-shots <폴더>) ─────────────
        // 사람 대신 대표 장면을 눌러 사진을 남긴다 (QA·보고서용). 실제 버튼과 같은 함수를 부름.

        IEnumerator ShotRun(string dir)
        {
            Directory.CreateDirectory(dir);
            var log = new StringBuilder();
            _seed = PickShotSeed();
            log.AppendLine($"seed {_seed}");
            int n = 0;
            IEnumerator Shot(string name)
            {
                yield return new WaitForSeconds(0.3f);
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{++n:00}_{name}.png"));
                log.AppendLine("shot " + name);
                yield return null;
            }
            // 오류 종류에 맞는 서류를 펼침 (사유서 오류 → 입성 사유서, 검역 오류 → 검역 소견서, 나머지 → 임시 통행증)
            int DocFor(ErrorKind e)
            {
                Doc want = e is ErrorKind.EntryMissing or ErrorKind.EntryNameMismatch or ErrorKind.EntryReasonDenied or ErrorKind.EntryDestMismatch ? Doc.Permit
                    : e is ErrorKind.QuarantineMissing or ErrorKind.QuarantineExpired ? Doc.Cert : Doc.Pass;
                return _docKinds.IndexOf(want); // 안 낸 서류면 -1 → 펼치지 않음
            }
            var seen = new HashSet<ErrorKind>();
            // C049: 그날 처음 나온 오류마다 서류를 펼친 장면 + 판정 띠 장면
            IEnumerator ReasonShots(string day)
            {
                yield return new WaitForSeconds(DropTime + DropStagger * 2);
                if (V.Reason != ErrorKind.OutsiderAppearance) { OpenDoc(DocFor(V.Reason)); yield return new WaitForSeconds(ZoomTime + 0.05f); }
                yield return Shot($"c049_{day}_{V.Reason}");
                CloseDoc();
                if (V.Reason is ErrorKind.PassMissing or ErrorKind.QuarantineMissing or ErrorKind.EntryMissing)
                {
                    // C056: 안 낸 서류는 칸 없음. 낸 서류 확대 · 검사 모드에서도 안 낸 서류 칸·글자 없음
                    log.AppendLine($"  C056 {day} {V.Reason} 작은 서류 칸 {string.Join(",", _docKinds)} (칸 수 {_tray.childCount})");
                    if (_docKinds.Count > 0) { OpenDoc(0); yield return new WaitForSeconds(ZoomTime + 0.05f); }
                    SetInspect(true);
                    yield return Shot($"c056_{day}_{V.Reason}_inspect");
                    SetInspect(false);
                    CloseDoc();
                }
                bool talk = V.Reason == ErrorKind.SpokenNameMismatch || V.Reason == ErrorKind.EntryDestMismatch || V.Reason == ErrorKind.AgeMismatch;
                if (talk) { SetLog(true); yield return Shot($"c051_{day}_{V.Reason}_log"); SetLog(false); } // 로그의 말과 서류 대조 (PDF 6쪽)
                if (V.Correct == Verdict.Kill)
                {
                    // C057: 제거 버튼을 실제로 눌러 판정 (얼굴 이상 → 제거 = 정답)
                    int before = _ledger.CorrectCount;
                    log.AppendLine($"  C057 {day} 제거 버튼 누를 수 있음 {_killBtn.interactable}, 글자 \"{_killBtn.GetComponentInChildren<Text>().text}\"");
                    _killBtn.onClick.Invoke();
                    log.AppendLine($"  C057 {day} 제거 클릭 → 정답 {V.Correct}, 정답 수 {before}→{_ledger.CorrectCount}");
                }
                else Decide(V.Correct);
                yield return Shot($"c049_{day}_{V.Reason}_result");
                if (talk) { SetLog(true); yield return Shot($"c051_{day}_{V.Reason}_log_after"); log.AppendLine($"  C051 판정 뒤 로그 줄 수 {_logContent.childCount}"); SetLog(false); } // 판정 줄 없음
            }
            Verdict Wrong(Visitor v) => v.Correct == Verdict.Pass ? Verdict.Deny : Verdict.Pass;
            // C057: 판정 버튼 순서(위 → 아래 = 화면 왼쪽부터 읽는 순서)와 신고 버튼 보임 여부
            void VerdictLog(string day)
            {
                var btns = new[] { _passBtn, _denyBtn, _killBtn, _reportBtn }
                    .Where(b => b.gameObject.activeInHierarchy)
                    .OrderByDescending(b => b.transform.position.y)
                    .Select(b => b.GetComponentInChildren<Text>().text.Replace("\n", " "));
                log.AppendLine($"  C057 {day} 판정 버튼 {string.Join(" > ", btns)} / 신고 보임 {_reportBtn.gameObject.activeInHierarchy} / 검사 모드 글자 \"{_inspectBtnText.text}\"");
            }

            yield return new WaitForSeconds(0.8f);
            yield return Shot("day1_intro"); // 1일차 안내: 통과·거부·제거, 주민등록 DB 없음
            StartDay();
            yield return new WaitForSeconds(DropTime + DropStagger);
            yield return Shot("day1_visitor"); // DB 탭 없음, 제거 버튼, 이름·나이를 말함
            VerdictLog("d1");
            SetLog(true);
            yield return Shot("c051_day1_log"); // 반투명 덮개 · 말풍선 (플레이어 우측, 방문자 좌측) · 스크롤바 없음
            SetLog(false);
            _passBtn.OnPointerEnter(new PointerEventData(EventSystem.current)); // 마우스 올림 상태 (그림 확인용)
            yield return Shot("day1_pass_hover");
            _passBtn.OnPointerExit(new PointerEventData(EventSystem.current));
            _inspectBtn.OnPointerEnter(new PointerEventData(EventSystem.current));
            yield return Shot("day1_inspect_off_hover");
            _inspectBtn.OnPointerExit(new PointerEventData(EventSystem.current));
            // C046: 검사 모드 버튼 끔↔켬·클릭 상태에서 글자가 그림의 글줄 칸과 같이 움직이는지
            {
                var ped = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
                string BtnLog() => $"그림 {_inspectBtn.image.overrideSprite?.name}, 글자 칸 y {_inspectBtnText.rectTransform.anchorMin.y:0.000}~{_inspectBtnText.rectTransform.anchorMax.y:0.000}, 글자 \"{_inspectBtnText.text}\"";
                yield return Shot("c046_inspect_off");
                log.AppendLine($"  C046 끔 기본: {BtnLog()}");
                _inspectBtn.OnPointerEnter(ped); _inspectBtn.OnPointerDown(ped);
                yield return Shot("c046_inspect_off_pressed");
                log.AppendLine($"  C046 끔 클릭 중: {BtnLog()}");
                _inspectBtn.OnPointerUp(ped); _inspectBtn.OnPointerClick(ped);
                yield return Shot("c046_inspect_on_hover");
                log.AppendLine($"  C046 클릭 뒤 켬 올림: {BtnLog()}");
                _inspectBtn.OnPointerDown(ped);
                yield return Shot("c046_inspect_on_pressed");
                log.AppendLine($"  C046 켬 클릭 중: {BtnLog()}");
                _inspectBtn.OnPointerUp(ped); _inspectBtn.OnPointerClick(ped); _inspectBtn.OnPointerExit(ped);
                EventSystem.current.SetSelectedGameObject(null);
                yield return Shot("c046_inspect_off_after_click");
                log.AppendLine($"  C046 클릭 뒤 끔: {BtnLog()}");
                ToggleInspect(); // Space 키와 같은 함수
                yield return Shot("c046_inspect_on_space");
                log.AppendLine($"  C046 Space 켬: {BtnLog()}");
                ToggleInspect();
                yield return Shot("c046_inspect_off_space");
                log.AppendLine($"  C046 Space 끔: {BtnLog()}");
            }
            // C050: 검사 모드 켜고 부스 초상 칸 클릭 → 초상 아래쪽 안내 줄
            SetInspect(true);
            OnCellClicked(_boothCell);
            yield return Shot("c050_inspect_line");
            log.AppendLine($"  C050 안내 줄: {_inspectLine.text}");
            SetInspect(false);
            _termTab = 1; RebuildTerminal();
            yield return Shot("day1_manual");
            int portraitShots = 0;
            while (_overlay == null)
            {
                // 초상 확인 (C038): 부스 초상 · 통행증 사진이 같은 인물인지, 나이·성별이 캡션과 맞는지
                if (portraitShots < 3 && Record != null)
                {
                    yield return new WaitForSeconds(DropTime);
                    OpenDoc(0);
                    yield return new WaitForSeconds(ZoomTime + 0.05f);
                    yield return Shot($"day1_portrait_{++portraitShots}");
                    if (portraitShots == 1)
                    {
                        // C045: 확대된 통행증 바탕을 다시 클릭 → 축소
                        ExecuteEvents.Execute(_docView.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
                        yield return new WaitForSeconds(ZoomTime + 0.1f);
                        log.AppendLine($"  C045 통행증 바탕 다시 클릭 → 열림 {DocOpen}, 크기 {_docView.localScale.x:0.00}");
                        yield return Shot("day1_pass_reclick_shrunk");
                        // 검사 모드 꺼진 채 통행증 칸(이름)을 클릭 → 축소
                        OpenDoc(0);
                        yield return new WaitForSeconds(ZoomTime + 0.1f);
                        var nameCell = _cells.First(c => c.Src == Source.TravelPass && c.Field == Field.Name);
                        ExecuteEvents.Execute(nameCell.Bg.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
                        yield return new WaitForSeconds(ZoomTime + 0.1f);
                        log.AppendLine($"  C045 통행증 칸 클릭(검사 모드 끔) → 열림 {DocOpen}");
                        // 기존 닫기 버튼
                        OpenDoc(0);
                        yield return new WaitForSeconds(ZoomTime + 0.1f);
                        ExecuteEvents.Execute(_docView.Find("Close").gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
                        yield return null;
                        log.AppendLine($"  C045 닫기 버튼 → 열림 {DocOpen}");
                        // 검사 모드 켠 채 칸 클릭 → 서류는 그대로, 칸 선택
                        OpenDoc(0);
                        yield return new WaitForSeconds(ZoomTime + 0.1f);
                        SetInspect(true);
                        nameCell = _cells.First(c => c.Src == Source.TravelPass && c.Field == Field.Name);
                        ExecuteEvents.Execute(nameCell.Bg.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
                        yield return new WaitForSeconds(ZoomTime + 0.1f);
                        log.AppendLine($"  C045 통행증 칸 클릭(검사 모드 켬) → 열림 {DocOpen}, 선택 {_pickA?.Field}");
                        SetInspect(false);
                    }
                    CloseDoc();
                    log.AppendLine($"  초상 {PortraitLog(Record)}");
                }
                log.AppendLine($"D1 {Who(V)} 말 \"{V.Line}\" 심은 오류 {V.Planted} → 정답 {V.Correct} ({V.Reason})");
                if (V.Reason != ErrorKind.None && seen.Add(V.Reason)) yield return ReasonShots("d1");
                else Decide(V.Correct);
                Next();
                yield return null;
            }
            yield return Shot("day1_settlement");

            // 2일차: DB 복구 · 통행증 범죄기록 칸 · 일부러 3번 틀려 "식량 감소" 정산
            CloseOverlay(); _dayIndex = 1; ShowDayIntro();
            yield return Shot("day2_intro");
            StartDay();
            yield return new WaitForSeconds(DropTime + DropStagger);
            yield return Shot("day2_docs");
            VerdictLog("d2");
            SetLog(true);
            yield return Shot("c051_day2_first_log"); // 새 일차 첫 방문자: 1일차 대화·날짜 줄 없이 지금 방문자만
            log.AppendLine($"  C051 2일차 첫 방문자 로그 줄 수 {_logContent.childCount}");
            SetLog(false);
            seen.Clear();
            int wrong = 0;
            bool shotCrime = false;
            while (_overlay == null)
            {
                log.AppendLine($"D2 {Who(V)} {V.Pass.Address} 심은 오류 {V.Planted} → 정답 {V.Correct} ({V.Reason})");
                if (!V.PassMissing && !shotCrime && V.Pass.CriminalRecord != null)
                {
                    yield return new WaitForSeconds(DropTime);
                    OpenDoc(0);
                    yield return new WaitForSeconds(ZoomTime + 0.05f);
                    yield return Shot("c049_d2_pass_crime"); // 통행증 범죄기록 칸
                    CloseDoc();
                    shotCrime = true;
                }
                if (V.Reason != ErrorKind.None && seen.Add(V.Reason)) yield return ReasonShots("d2");
                else if (wrong < 3) { Decide(Wrong(V)); wrong++; }
                else Decide(V.Correct);
                Next();
                yield return null;
            }
            yield return Shot("c049_d2_settlement_ration_cut");

            // 3일차: 입성 사유서 · 강00 귓속말 · 진00 선택 저장
            CloseOverlay(); _dayIndex = 2; ShowDayIntro();
            yield return Shot("day3_intro");
            StartDay();
            yield return new WaitForSeconds(DropTime + DropStagger * 2);
            yield return Shot("day3_docs"); // 임시 통행증 · 검역 소견서 · 입성 사유서
            VerdictLog("d3");
            seen.Clear();
            bool shotPermitOk = false;
            while (_overlay == null)
            {
                log.AppendLine($"D3 {Who(V)} 말 \"{V.Line}\" 사유서 {V.Permit?.Name}/{V.Permit?.Reason}/{V.Permit?.Destination} 심은 오류 {V.Planted} → 정답 {V.Correct} ({V.Reason})");
                if (V.Story == "kang")
                {
                    yield return new WaitForSeconds(DropTime + DropStagger * 2);
                    yield return Shot("c049_d3_kang");
                    SetLog(true);
                    yield return Shot("c049_d3_kang_log");
                    SetLog(false);
                    Decide(V.Correct);
                    yield return Shot("c049_d3_kang_result");
                }
                else if (V.Story == "jin")
                {
                    yield return new WaitForSeconds(DropTime + DropStagger * 2);
                    OpenDoc(_docKinds.IndexOf(Doc.Permit));
                    yield return new WaitForSeconds(ZoomTime + 0.05f);
                    yield return Shot("c049_d3_jin");
                    CloseDoc();
                    Decide(Verdict.Deny); // 선택 1) 강00의 말을 믿고 진00을 거부
                    yield return Shot("c049_d3_jin_choice");
                }
                else if (!shotPermitOk && V.Reason == ErrorKind.None && V.Permit != null)
                {
                    yield return new WaitForSeconds(DropTime + DropStagger * 2);
                    OpenDoc(_docKinds.IndexOf(Doc.Permit));
                    yield return new WaitForSeconds(ZoomTime + 0.05f);
                    yield return Shot("c049_d3_permit_ok");
                    CloseDoc();
                    shotPermitOk = true;
                    Decide(V.Correct);
                }
                else if (V.Reason != ErrorKind.None && seen.Add(V.Reason)) yield return ReasonShots("d3");
                else Decide(V.Correct);
                if (_vi == _visitors.Count - 1)
                {
                    SetLog(true);
                    yield return Shot("day3_log_full");
                    SetLog(false);
                }
                Next();
                yield return null;
            }
            yield return Shot("day3_settlement"); // 강00 이벤트 선택 줄
            log.AppendLine($"  강00 이벤트 선택 {_jinChoice}");

            // seed에서 안 나온 오류 장면: 첫 방문자를 그 오류로 바꿔 촬영
            foreach (var (di, k) in new[] { (0, ErrorKind.NotSeoul), (0, ErrorKind.PassMissing), (1, ErrorKind.PassDateWrong), (2, ErrorKind.EntryMissing), (2, ErrorKind.EntryReasonDenied) })
            {
                CloseOverlay(); _dayIndex = di; StartDay();
                _visitors[0] = new VisitorGenerator(_db, 977 + (int)k).Generate(Day, k);
                ShowVisitor();
                log.AppendLine($"  추가 장면 {di + 1}일차 {Who(V)} {V.Pass.Address} 말 \"{V.Line}\" → 정답 {V.Correct} ({V.Reason})");
                yield return ReasonShots($"d{di + 1}_extra");
            }

            // 실수 5번 → 퇴출 (1일차를 다시 시작해 일부러 모두 틀림)
            CloseOverlay(); _dayIndex = 0; _runTotal = 0; StartDay();
            while (_overlay == null) { Decide(Wrong(V)); Next(); yield return null; }
            log.AppendLine($"  퇴출 확인: 실수 {_ledger.Mistakes} → {_ledger.Stage}");
            yield return Shot("c049_fired_settlement");
            File.WriteAllText(Path.Combine(dir, "log.txt"), log.ToString());
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }

        // 사진에 필요한 장면이 나오는 seed 찾기: 1일차 오류 종류가 많고, 2일차 날짜·통행증 오류, 3일차 사유서 오류
        int PickShotSeed()
        {
            int best = 1, bestScore = -1;
            for (int s = 1; s < 400; s++)
            {
                var d1 = new VisitorGenerator(_db, s * 31 + 1).GenerateDay(_days[0]);
                var d2 = new VisitorGenerator(_db, s * 31 + 2).GenerateDay(_days[1]);
                var d3 = new VisitorGenerator(_db, s * 31 + 3).GenerateDay(_days[2]);
                int score = d1.Select(v => v.Reason).Where(r => r != ErrorKind.None).Distinct().Count() * 3
                    + (d1.Any(v => v.Reason == ErrorKind.OutsiderAppearance) ? 4 : 0)
                    + d2.Select(v => v.Reason).Where(r => r is ErrorKind.PassDateWrong or ErrorKind.PassMissing).Distinct().Count() * 2
                    + d3.Select(v => v.Reason).Where(r => r is ErrorKind.EntryMissing or ErrorKind.EntryNameMismatch or ErrorKind.EntryReasonDenied or ErrorKind.EntryDestMismatch).Distinct().Count() * 3
                    + (d2.Any(v => v.Pass.CriminalRecord != null && !v.PassMissing) ? 1 : 0);
                if (score > bestScore) { bestScore = score; best = s; }
            }
            return best;
        }

        static string PortraitLog(Resident r)
        {
            var e = Portraits.Get(r.Face.Portrait);
            return $"{r.Name} {r.ResidentId} 나이 {Portraits.Age(r.BirthDate)} {(Portraits.IsMale(r.ResidentId) ? "남" : "여")} → p{e.Id:00} \"{e.Caption}\"";
        }

        static string ArgStr(string key)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, key);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        static int ArgInt(string key, int def) => int.TryParse(ArgStr(key), out int v) ? v : def;
    }
}
