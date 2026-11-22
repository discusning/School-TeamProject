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
    //   우측 상단: 서류 검토 영역 (작은 서류가 위에서 내려옴 → 클릭하면 확대 · 미제출 표시)
    //             그날 규칙에 없는 서류(예: 검역 소견서)는 칸 자체를 보이지 않음 (3차 회의 261117)
    //   우측 하단: 단말기 (주민등록 DB · 규칙 및 매뉴얼)
    //   우측: 판정 버튼 (통과 · 거부 · 신고 · 제거)
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
        Button _passBtn, _denyBtn, _nextBtn;
        readonly List<Button> _termTabs = new();
        static readonly string[] DocNames = { "임시 통행증", "검역 소견서" };

        // 서류 연출 (3차 회의 261117): 작은 서류가 위에서 내려오고, 클릭하면 서류 검토 영역 가득 확대
        RectTransform _desk, _tray, _docView;
        readonly List<RectTransform> _smallDocs = new();
        Coroutine _dropCo, _zoomCo;
        const float DropTime = 0.7f, DropStagger = 0.15f, DropFrom = 620f, ZoomTime = 0.18f;
        bool DocOpen => _docView != null && _docView.gameObject.activeSelf;
        static readonly Color Soon = new(0.6f, 0.6f, 0.6f);
        RectTransform _overlay, _canvas;

        AudioSource _audio;
        AudioClip _stamp, _buzz, _ding, _tick;

        Visitor V => _visitors[_vi];
        Resident Record => _db.FindByName(V.Pass.Name);
        DayConfig Day => _days[_dayIndex];

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
            Ui.Label(panel.transform, "Brief", 0.08f, 0.38f, 0.92f, 0.78f, Day.Briefing, 30, Ui.Light, TextAnchor.MiddleCenter);
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
            if (_dayIndex == 0) ClearLog();
            AddLog($"── {Day.Day}일차 · {Fmt.Date(Day.Today)} ──", new Color(0.7f, 0.7f, 0.7f));
            ShowVisitor();
        }

        void ShowVisitor()
        {
            _decided = false;
            SetInspect(false);
            _counter.text = $"방문자 {_vi + 1} / {_visitors.Count}";
            _speech.text = V.Line;
            FaceView.Draw(_boothFace, V.Face, _boothBg != null ? Color.clear : Ui.Booth * 1.2f); // 배경 그림이 있으면 얼굴 뒤로 검문소가 보임
            _termTab = 0;
            CloseDoc();
            BuildSmallDocs();
            RebuildTerminal();
            SetBand(false);
            SetLog(false);
            AddLog($"[방문자 {_vi + 1}] {V.Pass.Name}\n“{V.Line}”", Color.white);
            _passBtn.interactable = _denyBtn.interactable = true;
            _nextBtn.gameObject.SetActive(false);
            Play(_tick);
        }

        void Decide(Verdict chosen)
        {
            if (_decided || _overlay != null) return;
            _decided = true;
            _ledger.Record(V, chosen);
            bool ok = chosen == V.Correct;
            Play(_stamp);
            Play(ok ? _ding : _buzz, 0.12f);
            string why = V.Correct == Verdict.Pass
                ? (ok ? "문제 없는 방문자" : "이 방문자는 문제가 없었습니다")
                : (ok ? "이유: " : "놓친 것: ") + Fmt.ErrorText(V.Reason);
            // 판정 결과: 초상 아래쪽 띠(바로 보임) + 대화 로그(다시 볼 수 있음)
            if (_bandOk != null)
            {
                _verdictBand.sprite = ok ? _bandOk : _bandBad;
                _verdictBand.color = Color.white;
                _verdictText.text = $"{(ok ? "정답" : "오답")} — 처리 {Name(chosen)} / 정답 {Name(V.Correct)}";
                _verdictWhy.text = why;
            }
            else
            {
                _verdictBand.color = ok ? Ui.Solid(Ui.PassGreen * 0.85f) : Ui.Solid(Ui.DenyRed * 0.85f);
                _verdictText.text = $"{(ok ? "정답" : "오답")} — 처리 {Name(chosen)} / 정답 {Name(V.Correct)}\n{why}";
            }
            SetBand(true);
            AddLog($"{(ok ? "○ 정답" : "× 오답")} — 처리 {Name(chosen)} / 정답 {Name(V.Correct)} · {why}", Color.white);
            _passBtn.interactable = _denyBtn.interactable = false;
            _nextBtn.gameObject.SetActive(true);
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
            Ui.Label(t, "Title", 0.05f, 0.9f, 0.95f, 0.98f, $"{Day.Day}일차 정산", 46, Ui.Light, TextAnchor.MiddleCenter);
            float y = 0.86f, step = Mathf.Min(0.07f, 0.5f / Mathf.Max(1, _ledger.Entries.Count));
            foreach (var e in _ledger.Entries)
            {
                string mark = e.Correct ? "○" : "×";
                string why = e.Visitor.Correct == Verdict.Pass ? "문제 없음" : Fmt.ErrorText(e.Visitor.Reason);
                var col = e.Correct ? Ui.Light : new Color(1f, 0.55f, 0.5f);
                Ui.Label(t, "Mark", 0.06f, y - step, 0.1f, y, mark, 24, col, TextAnchor.MiddleCenter);
                Ui.Label(t, "Name", 0.11f, y - step, 0.24f, y, e.Visitor.Pass.Name, 24, col);
                Ui.Label(t, "Verdict", 0.24f, y - step, 0.5f, y, $"처리 {Name(e.Chosen)} · 정답 {Name(e.Visitor.Correct)}", 24, col);
                Ui.Label(t, "Why", 0.5f, y - step, 0.95f, y, why, 24, col);
                y -= step;
            }
            Ui.Label(t, "Sum", 0.06f, 0.2f, 0.94f, 0.32f,
                $"정답 {_ledger.CorrectCount} / {_ledger.Entries.Count} · 실수 {_ledger.Mistakes} (하루 {DayLedger.FreeWarnings}번까지 경고)\n" +
                $"배급 +{_ledger.Earned}  벌점 −{_ledger.Penalty}  =  {_ledger.Total}   (누적 {_runTotal})", 28, Ui.Pick, TextAnchor.MiddleCenter);
            Ui.Label(t, "Note", 0.06f, 0.14f, 0.94f, 0.19f, "※ 배급·벌점 숫자는 임시입니다 (기획 확정 전)", 20, new Color(0.7f, 0.7f, 0.7f), TextAnchor.MiddleCenter);
            bool last = _dayIndex >= _days.Count - 1;
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
                ? (on ? "검사 모드 켬" : "검사 모드 끔")
                : (on ? "검사 모드: 켬 [Space]" : "검사 모드: 끔 [Space]");
            _inspectLine.text = on ? "검사 모드 — 서류 칸 하나, DB 칸 하나를 차례로 클릭" : "";
        }

        void OnCellClicked(FieldCell cell)
        {
            if (_overlay != null) return;
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
                Ui.Box(npc, "Shade", 0, 0, 1, 0.55f, new Color(0f, 0f, 0f, 0.6f)).raycastTarget = false;
                Ui.Box(npc, "ShadeTop", 0, 0.95f, 1, 1, new Color(0f, 0f, 0f, 0.5f)).raycastTarget = false;
            }
            Ui.Label(npc, "Zone", 0.03f, 0.955f, 0.97f, 0.995f, "NPC 영역 — 방문자 얼굴 · 대화", 20, Soon);
            // 초상: NPC 영역 위쪽 폭 전체. 얼굴은 비율을 지켜 가운데 (서류·DB 사진과 같은 모양으로 비교)
            _boothCell = MakeCell(npc, 0.04f, 0.55f, 0.96f, 0.95f, Source.Booth, Field.Photo, null, _boothBg != null ? Color.clear : Ui.Booth * 1.2f, Ui.Light, out _);
            var faceArea = Ui.Rect(_boothCell.Bg.transform, "FaceArea", 0, 0, 1, 1, 6);
            _boothFace = Ui.Rect(faceArea, "Face", 0, 0, 1, 1);
            var fit = _boothFace.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = FaceView.PortraitAspect; // 초상 그림 3:4
            // 판정 결과 띠: 초상 바깥 아래, 판정 뒤 첫마디 칸 위에 뜸 (첫마디는 대화 로그에 남음). 그림 비율 유지, 글자는 그림의 글줄 칸 2개에 씀
            _bandOk = Ui.Art("band_ok");
            _bandBad = Ui.Art("band_bad");
            if (_bandOk != null)
            {
                var band = Ui.Fit(npc, "VerdictBandArea", 0.02f, 0.33f, 0.98f, 0.535f, _bandOk.rect.width / _bandOk.rect.height);
                _verdictBand = band.gameObject.AddComponent<Image>();
                _verdictBand.sprite = _bandOk;
                _verdictBand.raycastTarget = false;
                _verdictText = Ui.Label(band, "Text", 0.25f, 0.51f, 0.92f, 0.7f, "", 22, Color.white, TextAnchor.MiddleLeft);
                _verdictWhy = Ui.Label(band, "Why", 0.25f, 0.25f, 0.92f, 0.44f, "", 20, Color.white, TextAnchor.MiddleLeft);
                Ui.Shrink(_verdictText, 12); Ui.Shrink(_verdictWhy, 12);
            }
            else
            {
                _verdictBand = Ui.Box(npc, "VerdictBand", 0.04f, 0.55f, 0.96f, 0.65f, Ui.PassGreen);
                _verdictBand.raycastTarget = false;
                _verdictText = Ui.Label(_verdictBand.transform, "Text", 0.02f, 0, 0.98f, 1, "", 22, Color.white, TextAnchor.MiddleCenter);
            }
            _verdictBand.gameObject.SetActive(false);

            _who = Ui.Label(npc, "Who", 0.04f, 0.48f, 0.96f, 0.535f, "대화창 — 방문자의 첫마디", 20, new Color(0.7f, 0.7f, 0.7f));
            var bubble = Ui.Box(npc, "Bubble", 0.04f, 0.33f, 0.96f, 0.48f, Ui.Light).transform;
            _bubble = bubble.gameObject;
            _speech = Ui.Label(bubble, "Text", 0, 0, 1, 1, "", 30, Ui.Ink, TextAnchor.MiddleLeft, 20);

            Ui.Button(npc, "LogOpen", 0.04f, 0.19f, 0.96f, 0.3f, "대화 로그 [Tab]", new Color(0.06f, 0.06f, 0.07f), Color.white, 26, () => { SetLog(true); Play(_tick); });
            _inspectLine = Ui.Label(npc, "Inspect", 0.04f, 0.01f, 0.96f, 0.17f, "", 24, Ui.Pick, TextAnchor.MiddleLeft);
            (_bandOk != null ? _verdictBand.transform.parent : _verdictBand.transform).SetAsLastSibling(); // 띠는 대화창 위에 뜸
            BuildLog(npc);

            // ② 서류 검토 영역 (우측 상단): 방문자가 낸 작은 서류가 위에서 내려옴 → 클릭하면 확대 (닫기 · Esc)
            //    그날 필요한 서류만 칸이 생김. 필요한데 안 냈으면 빈 칸에 "미제출"
            var deskImg = Ui.Box(root, "DocArea", 0.405f, 0.455f, 0.8f, 0.95f, Ui.Desk);
            deskImg.gameObject.AddComponent<RectMask2D>(); // 위에서 내려오는 서류가 영역 밖에서는 안 보이게
            _desk = deskImg.rectTransform;
            var desk = _desk.transform;
            Ui.Label(desk, "Zone", 0.03f, 0.9f, 0.97f, 0.99f, "서류 검토 — 서류를 클릭하면 크게 봅니다", 20, Soon);
            _tray = Ui.Rect(desk, "Tray", 0, 0, 1, 0.9f);
            _docView = (RectTransform)Ui.Box(desk, "OpenDoc", 0.02f, 0.02f, 0.98f, 0.98f, Ui.Paper).transform;
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
            Ui.Button(verdict, "Report", 0.06f, 0.54f, 0.94f, 0.65f, "신고\n(준비 중)", Ui.Disabled, Soon, 24, null).interactable = false;
            Ui.Button(verdict, "Remove", 0.06f, 0.42f, 0.94f, 0.53f, "제거\n(준비 중)", Ui.Disabled, Soon, 24, null).interactable = false;
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
                Ui.Label(verdict, "InspectKey", 0.06f, 0.19f, 0.94f, 0.225f, "[Space] 키로 켜고 끔", 18, Soon, TextAnchor.MiddleCenter);
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
        // 서류가 빠졌는지: 검역 소견서가 필요한 날인데 안 냈을 때
        bool CertMissing => V.Cert == null && CertDay;

        // 작은 서류 칸을 만들고 위에서 아래로 내려보냄. 낸 서류만 내려오고, 미제출 칸은 빈 자리로 바로 보임.
        void BuildSmallDocs()
        {
            if (_dropCo != null) StopCoroutine(_dropCo);
            Ui.Clear(_tray);
            _smallDocs.Clear();
            var drop = new List<RectTransform>();
            int slots = CertDay ? 2 : 1;
            for (int i = 0; i < slots; i++)
            {
                bool missing = i == 1 && CertMissing;
                var d = MakeSmallDoc(i, missing);
                _smallDocs.Add(d);
                if (!missing) drop.Add(d);
            }
            _dropCo = StartCoroutine(DropDocs(drop));
        }

        RectTransform MakeSmallDoc(int i, bool missing)
        {
            int k = i;
            var passArt = Ui.Art("pass_card");
            if (i == 0 && !missing && passArt != null)
            {
                // 임시 통행증: 받은 카드 그림 (가로 카드). 제목은 그림 위 초록 띠 글줄 칸에
                var area = Ui.Rect(_tray, "SmallDoc0", 0.03f, 0.3f, 0.47f, 0.9f);
                var fit = Ui.Fit(area, "Card", 0, 0, 1, 1, PassAspect);
                var card = Ui.Button(fit, "Button", 0, 0, 1, 1, "", Color.white, Ui.Ink, 18, () => OpenDoc(k));
                card.image.sprite = passArt;
                Destroy(card.GetComponentInChildren<Text>().gameObject);
                Ui.Label(card.transform, "Title", 0.18f, 0.838f, 0.82f, 0.914f, DocNames[0], 16, Ui.Ink, TextAnchor.MiddleCenter);
                Ui.Label(card.transform, "Hint", 0.08f, 0.02f, 0.5f, 0.17f, "클릭", 16, Ui.Ink * 2.2f, TextAnchor.MiddleLeft);
                return area;
            }
            float x0 = passArt != null ? 0.52f + (i - 1) * 0.27f : 0.05f + i * 0.27f;
            var b = Ui.Button(_tray, "SmallDoc" + i, x0, 0.08f, x0 + 0.22f, 0.86f, "",
                missing ? new Color(0f, 0f, 0f, 0.28f) : Ui.Paper, Ui.Ink, 18, () => OpenDoc(k));
            var t = b.transform;
            if (missing)
            {
                Ui.Label(t, "Title", 0.06f, 0.78f, 0.94f, 0.97f, DocNames[i], 20, Ui.Light, TextAnchor.MiddleCenter);
                Ui.Label(t, "Missing", 0.06f, 0.3f, 0.94f, 0.6f, "미제출", 28, new Color(1f, 0.5f, 0.45f), TextAnchor.MiddleCenter);
                return b.image.rectTransform;
            }
            Ui.Label(t, "Title", 0.06f, 0.8f, 0.94f, 0.97f, DocNames[i], 20, Ui.Ink, TextAnchor.MiddleCenter);
            var line = Ui.Solid(Ui.Paper * 0.75f);
            if (i == 0) // 통행증: 사진 자리 + 글줄 + 도장
            {
                Ui.Box(t, "Photo", 0.1f, 0.5f, 0.42f, 0.76f, line);
                for (int r = 0; r < 4; r++) Ui.Box(t, "Line" + r, 0.48f, 0.72f - r * 0.065f, 0.9f, 0.745f - r * 0.065f, line);
                for (int r = 0; r < 2; r++) Ui.Box(t, "Line" + (r + 4), 0.1f, 0.4f - r * 0.065f, 0.9f, 0.425f - r * 0.065f, line);
            }
            else // 검역 소견서: 글줄 + 검사 칸 3개
            {
                for (int r = 0; r < 3; r++) Ui.Box(t, "Line" + r, 0.1f, 0.72f - r * 0.065f, 0.9f, 0.745f - r * 0.065f, line);
                for (int r = 0; r < 3; r++) Ui.Box(t, "Box" + r, 0.1f, 0.42f - r * 0.1f, 0.9f, 0.5f - r * 0.1f, line);
            }
            Ui.Circle(t, "Stamp", 0.56f, 0.05f, 0.9f, 0.2f, new Color(0.7f, 0.12f, 0.1f, 0.75f));
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
            _zoomCo = StartCoroutine(ZoomFrom(_smallDocs[i]));
            Play(_tick);
        }

        void CloseDoc()
        {
            if (_zoomCo != null) { StopCoroutine(_zoomCo); _zoomCo = null; }
            _tab = -1;
            if (_pickA != null && (_pickA.Src == Source.TravelPass || _pickA.Src == Source.Quarantine)) SetInspect(_inspect);
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
            _zoomCo = null;
        }

        void RebuildDoc()
        {
            _cells.RemoveAll(c => c.Src == Source.TravelPass || c.Src == Source.Quarantine);
            Ui.Clear(_docContent);
            if (_tab < 0) return;
            var t = _docContent.transform;
            var passArt = Ui.Art("pass_card");
            _docView.GetComponent<Image>().color = _tab == 0 && passArt != null ? Color.clear : Ui.Paper;
            if (_tab == 0 && passArt != null)
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
                (Field f, string v)[] rows =
                {
                    (Field.Name, p.Name), (Field.ResidentId, p.ResidentId), (Field.BirthDate, Fmt.Date(p.BirthDate)),
                    (Field.Address, p.Address), (Field.IssueDate, Fmt.Date(p.IssueDate)),
                };
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
            else if (_tab == 0)
            {
                var p = V.Pass;
                Ui.Label(t, "Title", 0.03f, 0.88f, 0.97f, 0.99f, "임시 통행증 — 1차 선별소 발급", 24, Ui.Ink);
                var photo = MakeCell(t, 0.03f, 0.4f, 0.24f, 0.86f, Source.TravelPass, Field.Photo, null, Ui.Solid(Ui.Paper * 0.9f), Ui.Ink, out _);
                FaceView.Draw(Ui.Rect(photo.Bg.transform, "Face", 0, 0, 1, 1, 4), p.Photo, new Color(0.8f, 0.8f, 0.78f));
                DocRow(t, 0.27f, 0.86f, Source.TravelPass, Field.Name, p.Name);
                DocRow(t, 0.27f, 0.74f, Source.TravelPass, Field.ResidentId, p.ResidentId);
                DocRow(t, 0.27f, 0.62f, Source.TravelPass, Field.BirthDate, Fmt.Date(p.BirthDate));
                DocRow(t, 0.27f, 0.50f, Source.TravelPass, Field.Address, p.Address);
                DocRow(t, 0.03f, 0.36f, Source.TravelPass, Field.IssueDate, Fmt.Date(p.IssueDate));
                MakeCell(t, 0.03f, 0.04f, 0.6f, 0.22f, Source.TravelPass, Field.Stamp, p.Stamp, Ui.Solid(Ui.Paper * 0.95f), new Color(0.7f, 0.12f, 0.1f), out var st);
                st.fontSize = 28;
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
                if (c == null)
                {
                    bool needed = CertMissing;
                    Ui.Label(t, "None", 0.03f, 0.45f, 0.54f, 0.82f, needed ? "미제출\n제출된 검역 소견서 없음" : "오늘은 검역 소견서를\n확인하지 않습니다", 26, needed ? Ui.DenyRed : Ui.Ink, TextAnchor.MiddleCenter);
                    if (needed)
                        Ui.Button(t, "Request", 0.08f, 0.2f, 0.5f, 0.36f, "방문자에게 서류 요구\n(준비 중)", Ui.Disabled, Soon, 20, null).interactable = false;
                    return;
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
            if (_termTab == 1) { BuildManual(t); return; }
            var r = Record;
            var dbArt = Ui.Art(r == null ? "db_none" : "db_screen");
            if (dbArt != null)
            {
                BuildDbScreen(t, r, dbArt);
                return;
            }
            Ui.Label(t, "Search", 0.02f, 0.88f, 0.98f, 0.99f, $"이름 검색 > {V.Pass.Name}_", 22, Ui.TermText * 0.85f);
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
            Ui.Label(scr, "Search", 0.12f, 0.814f, 0.95f, 0.922f, $"이름 검색 > {V.Pass.Name}_", 22, Ui.TermText);
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
            var docs = "임시 통행증" + (Day.Has(ErrorKind.QuarantineMissing) || Day.Has(ErrorKind.QuarantineExpired) ? " · 검역 소견서" : "");
            var sb = new StringBuilder();
            sb.AppendLine("■ 오늘의 규칙: " + Day.RuleShort);
            sb.AppendLine("■ 필수 제출 서류: " + docs);
            if (Day.Has(ErrorKind.ForgedStamp)) sb.AppendLine($"■ 정상 도장: 「{Rules.GenuineStamp}」");
            if (Day.Has(ErrorKind.QuarantineExpired)) sb.AppendLine($"■ 검역 소견서 검사일: 오늘 기준 {Rules.QuarantineValidDays}일 이내");
            Ui.Label(t, "Rules", 0.03f, 0.42f, 0.97f, 0.98f, sb.ToString(), 22, Ui.TermText, TextAnchor.UpperLeft);
            Ui.Label(t, "Forge", 0.03f, 0.22f, 0.97f, 0.4f, "■ 위조 서류 판별 예시 (준비 중)", 22, Ui.TermText * 0.6f, TextAnchor.MiddleLeft);
            Ui.Label(t, "Quar", 0.03f, 0.03f, 0.97f, 0.21f, "■ 검역 소견서 판독 기준 (준비 중)", 22, Ui.TermText * 0.6f, TextAnchor.MiddleLeft);
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
        // 왼쪽 NPC 영역을 검정 불투명으로 덮고 흰 글씨. 방문자 첫마디 · 판정 결과가 위에서 아래로 쌓이고 스크롤된다.
        // 열기: 버튼 또는 Tab. 닫기: 닫기 버튼 또는 Tab. 새 방문자가 오면 닫힘. 1일차 시작 때 비움.

        void BuildLog(Transform npc)
        {
            var panel = Ui.Box(npc, "Log", 0, 0, 1, 1, Color.black);
            _logPanel = panel.rectTransform;
            var t = panel.transform;
            Ui.Label(t, "Title", 0.04f, 0.94f, 0.7f, 0.99f, "대화 로그", 30, Color.white);
            Ui.Button(t, "Close", 0.72f, 0.94f, 0.96f, 0.99f, "닫기 [Tab]", new Color(0.2f, 0.2f, 0.2f), Color.white, 22, () => { SetLog(false); Play(_tick); });

            var view = Ui.Box(t, "View", 0.04f, 0.02f, 0.93f, 0.93f, new Color(0, 0, 0, 0));
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

            var track = Ui.Box(t, "Bar", 0.945f, 0.02f, 0.97f, 0.93f, new Color(1, 1, 1, 0.12f));
            var area = Ui.Rect(track.transform, "Area", 0, 0, 1, 1);
            var handle = Ui.Box(area, "Handle", 0, 0, 1, 1, new Color(1, 1, 1, 0.6f));
            var bar = track.gameObject.AddComponent<Scrollbar>();
            bar.handleRect = handle.rectTransform;
            bar.targetGraphic = handle;
            bar.direction = Scrollbar.Direction.BottomToTop;
            _logScroll.verticalScrollbar = bar;
            _logScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            panel.gameObject.SetActive(false);
        }

        void SetLog(bool on)
        {
            if (_logPanel == null) return;
            _logPanel.gameObject.SetActive(on);
            if (on) ScrollLogToEnd();
        }

        void AddLog(string text, Color color)
        {
            Ui.Label(_logContent, "Line", 0, 0, 1, 1, text, 24, color, TextAnchor.UpperLeft);
            if (LogOpen) ScrollLogToEnd();
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

        static string Name(Verdict v) => v switch { Verdict.Pass => "통과", Verdict.Deny => "거부", _ => "신고" };

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

            yield return new WaitForSeconds(0.8f);
            yield return Shot("day1_intro");
            StartDay();
            yield return new WaitForSeconds(DropTime + DropStagger);
            yield return Shot("day1_visitor"); // 1일차 서류 칸: 임시 통행증만 (검역 소견서 칸 없음)
            _passBtn.OnPointerEnter(new PointerEventData(EventSystem.current)); // 마우스 올림 상태 (그림 확인용)
            yield return Shot("day1_pass_hover");
            _passBtn.OnPointerExit(new PointerEventData(EventSystem.current));
            _inspectBtn.OnPointerEnter(new PointerEventData(EventSystem.current));
            yield return Shot("day1_inspect_off_hover");
            _inspectBtn.OnPointerExit(new PointerEventData(EventSystem.current));
            _termTab = 1; RebuildTerminal();
            yield return Shot("day1_manual");
            _termTab = 0; RebuildTerminal();
            bool shotMismatch = false, shotWrong = false, shotNoDb = false;
            int portraitShots = 0;
            while (_overlay == null)
            {
                // 초상 확인 (C038): 부스 초상 · 통행증 사진 · DB 사진이 같은 인물인지, 나이·성별이 캡션과 맞는지
                if (portraitShots < 3 && Record != null)
                {
                    yield return new WaitForSeconds(DropTime);
                    OpenDoc(0);
                    yield return new WaitForSeconds(ZoomTime + 0.05f);
                    yield return Shot($"day1_portrait_{++portraitShots}");
                    CloseDoc();
                    log.AppendLine($"  초상 {PortraitLog(Record)}");
                }
                if (!shotNoDb && V.Reason == ErrorKind.NotInDatabase)
                {
                    yield return new WaitForSeconds(DropTime);
                    yield return Shot("day1_db_none"); // DB 검색 결과 없음
                    shotNoDb = true;
                }
                if (!shotMismatch && (V.Reason == ErrorKind.BirthMismatch || V.Reason == ErrorKind.AddressMismatch || V.Reason == ErrorKind.IdDigitMismatch))
                {
                    var f = V.Reason == ErrorKind.BirthMismatch ? Field.BirthDate : V.Reason == ErrorKind.AddressMismatch ? Field.Address : Field.ResidentId;
                    yield return new WaitForSeconds(DropTime);
                    OpenDoc(0); // 서류 칸은 서류를 확대해야 생김
                    yield return new WaitForSeconds(ZoomTime + 0.05f);
                    SetInspect(true);
                    OnCellClicked(_cells.First(c => c.Src == Source.TravelPass && c.Field == f));
                    OnCellClicked(_cells.First(c => c.Src == Source.Database && c.Field == f));
                    yield return Shot("day1_inspect_mismatch");
                    _inspectBtn.OnPointerEnter(new PointerEventData(EventSystem.current));
                    yield return Shot("day1_inspect_on_hover");
                    _inspectBtn.OnPointerExit(new PointerEventData(EventSystem.current));
                    Decide(V.Correct);
                    yield return Shot("day1_result_correct"); // 통과·거부 비활성 + 판정 띠 정답
                    shotMismatch = true;
                }
                else if (!shotWrong && V.Correct == Verdict.Pass && shotMismatch)
                {
                    Decide(Verdict.Deny);
                    yield return Shot("day1_result_wrong");
                    SetLog(true);
                    yield return Shot("day1_log");
                    SetLog(false);
                    shotWrong = true;
                }
                else Decide(V.Correct);
                log.AppendLine($"D1 {V.Pass.Name} 심은 오류 {V.Planted} → 정답 {V.Correct} ({V.Reason})");
                Next();
                yield return null;
            }
            yield return Shot("day1_settlement");

            CloseOverlay(); _dayIndex = 1; StartDay();
            yield return new WaitForSeconds(DropTime + DropStagger);
            yield return Shot("day2_docs");
            bool shotOutsider = false;
            while (_overlay == null)
            {
                if (!shotOutsider && V.Planted == ErrorKind.OutsiderAppearance)
                {
                    SetInspect(true);
                    OnCellClicked(_boothCell);
                    OnCellClicked(_cells.First(c => c.Src == Source.Database && c.Field == Field.Photo));
                    yield return Shot("day2_outsider_inspect");
                    log.AppendLine($"  외부인 초상 {PortraitLog(Record)} · 부스 얼굴 {V.Face} / DB {Record.Face}");
                    shotOutsider = true;
                }
                log.AppendLine($"D2 {V.Pass.Name} 심은 오류 {V.Planted} → 정답 {V.Correct} ({V.Reason})");
                Decide(V.Correct);
                Next();
                yield return null;
            }
            CloseOverlay(); _dayIndex = 2; ShowDayIntro();
            yield return Shot("day3_intro");
            StartDay();
            yield return new WaitForSeconds(DropTime + DropStagger);
            yield return Shot("day3_docs"); // 검역 소견서가 처음 나오는 날: 칸이 생김
            OpenDoc(1);
            yield return Shot("day3_cert_tab");
            CloseDoc();
            bool shotCertMissing = false, shotCrime = false;
            while (_overlay == null)
            {
                if (!shotCrime && Record?.CriminalRecord != null && Day.Has(ErrorKind.CriminalRecord))
                {
                    yield return new WaitForSeconds(DropTime);
                    yield return Shot("day3_db_crime"); // 범죄 이력 칸: 기록 있음
                    shotCrime = true;
                }
                if (!shotCertMissing && V.Cert == null)
                {
                    yield return new WaitForSeconds(DropTime);
                    yield return Shot("day3_cert_missing"); // 이미 나온 서류를 안 냄: 빈 칸 "미제출"
                    OpenDoc(1);
                    yield return Shot("day3_cert_missing_open");
                    CloseDoc();
                    shotCertMissing = true;
                }
                log.AppendLine($"D3 {V.Pass.Name} 심은 오류 {V.Planted} → 정답 {V.Correct} ({V.Reason})");
                Decide(V.Correct);
                if (_vi == _visitors.Count - 1)
                {
                    SetLog(true);
                    yield return Shot("day3_log_full");
                    _logScroll.verticalNormalizedPosition = 0.5f;
                    yield return Shot("day3_log_scrolled");
                }
                Next();
                yield return null;
            }
            yield return Shot("day3_settlement");
            File.WriteAllText(Path.Combine(dir, "log.txt"), log.ToString());
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }

        // 사진에 필요한 장면(1일차 DB 불일치, 2일차 외부인, 3일차 검역 소견서 미제출)이 나오는 seed 찾기
        int PickShotSeed()
        {
            for (int s = 1; s < 500; s++)
            {
                var d1 = new VisitorGenerator(_db, s * 31 + 1).GenerateDay(_days[0]);
                var d2 = new VisitorGenerator(_db, s * 31 + 2).GenerateDay(_days[1]);
                bool mismatch = d1.Any(v => v.Reason is ErrorKind.BirthMismatch or ErrorKind.AddressMismatch or ErrorKind.IdDigitMismatch);
                bool cleanAfter = d1.SkipWhile(v => v.Reason is not (ErrorKind.BirthMismatch or ErrorKind.AddressMismatch or ErrorKind.IdDigitMismatch)).Skip(1).Any(v => v.Correct == Verdict.Pass);
                var d3 = new VisitorGenerator(_db, s * 31 + 3).GenerateDay(_days[2]);
                bool noDb = d1.Any(v => v.Reason == ErrorKind.NotInDatabase);
                if (mismatch && cleanAfter && noDb && d2.Any(v => v.Planted == ErrorKind.OutsiderAppearance) && d3.Any(v => v.Cert == null)) return s;
            }
            return 1;
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
