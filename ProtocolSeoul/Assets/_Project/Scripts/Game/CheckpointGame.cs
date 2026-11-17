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
    //   좌측: NPC 영역 (얼굴 · 첫마디 · 심문/서류 요청 자리 · 판정 결과)
    //   우측 상단: 서류 검토 영역 (임시 통행증 · 검역 소견서 · 미제출 표시)
    //   우측 하단: 단말기 (주민등록 DB · 규칙 및 매뉴얼)
    //   우측: 판정 버튼 (통과 · 거부 · 신고 · 제거)
    // 기획에 있으나 로직이 없는 기능은 자리만 두고 "준비 중"으로 표시.
    // 흐름: 하루 시작 → 방문자 → (검사 모드로 서류 칸 ↔ DB 칸 비교) → 통과/거부 → 맞음·틀림 → 다음 → 하루 정산 → 다음 날
    // 판정 기준은 Core/Rules.cs 한 곳. 이 파일은 보여 주고 입력만 받는다.
    public class CheckpointGame : MonoBehaviour
    {
        class FieldCell { public Source Src; public Field Field; public Image Bg; public Color Normal; }

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

        Text _header, _counter, _rule, _speech, _resultTitle, _resultBody, _inspectLine, _inspectBtnText;
        RectTransform _boothFace, _docContent, _termContent;
        Image _resultBox;
        Button _passBtn, _denyBtn, _nextBtn;
        readonly List<Button> _tabs = new(), _termTabs = new();
        readonly List<Text> _tabTexts = new();
        static readonly string[] DocTabNames = { "임시 통행증", "검역 소견서" };
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
            ShowVisitor();
        }

        void ShowVisitor()
        {
            _decided = false;
            SetInspect(false);
            _counter.text = $"방문자 {_vi + 1} / {_visitors.Count}";
            _speech.text = V.Line;
            FaceView.Draw(_boothFace, V.Face, Ui.Booth * 1.2f);
            _tab = 0;
            _termTab = 0;
            RebuildDoc();
            RebuildTerminal();
            _resultBox.color = Ui.Booth * 1.25f;
            _resultTitle.text = "판정 대기";
            _resultTitle.color = Ui.Light;
            _resultBody.text = "서류와 DB를 확인하고\n최종 처분을 고르세요.";
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
            _resultBox.color = ok ? Ui.PassGreen * 0.8f : Ui.DenyRed * 0.8f;
            _resultTitle.text = ok ? "정답" : "오답";
            _resultTitle.color = Color.white;
            string why = V.Correct == Verdict.Pass
                ? (ok ? "문제 없는 방문자" : "이 방문자는 문제가 없었습니다")
                : (ok ? "이유: " : "놓친 것: ") + Fmt.ErrorText(V.Reason);
            _resultBody.text = $"처리: {Name(chosen)}  /  정답: {Name(V.Correct)}\n{why}";
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
            foreach (var c in _cells.Append(_boothCell)) if (c != null && c.Bg != null) c.Bg.color = c.Normal;
            _inspectBtnText.text = on ? "검사 모드: 켬 [Space]" : "검사 모드: 끔 [Space]";
            _inspectLine.text = on ? "검사 모드 — 서류 칸 하나, DB 칸 하나를 차례로 클릭" : "";
        }

        void OnCellClicked(FieldCell cell)
        {
            if (_overlay != null) return;
            if (!_inspect) { _inspectLine.text = "칸을 비교하려면 검사 모드를 켜세요 [Space]"; return; }
            if (_pickA == null)
            {
                foreach (var c in _cells.Append(_boothCell)) if (c?.Bg != null) c.Bg.color = c.Normal;
                _pickA = cell;
                cell.Bg.color = Ui.Pick;
                _inspectLine.text = $"「{Fmt.FieldName(cell.Field)}」 선택 — 비교할 칸을 하나 더 고르세요";
                Play(_tick);
                return;
            }
            if (_pickA == cell) { cell.Bg.color = cell.Normal; _pickA = null; _inspectLine.text = "선택 취소"; return; }

            var result = Inspector.Compare(V, Record, _pickA.Src, _pickA.Field, cell.Src, cell.Field);
            cell.Bg.color = Ui.Pick;
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
                    _pickA.Bg.color = cell.Bg.color = new Color(1f, 0.55f, 0.45f);
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

            // ① NPC 영역 (좌측): 얼굴 대조 · 첫마디 · 심문/서류 요청 · 판정 결과
            var npc = Ui.Box(root, "NpcArea", 0, 0, 0.4f, 0.95f, Ui.Booth).transform;
            Ui.Label(npc, "Zone", 0.03f, 0.955f, 0.97f, 0.995f, "NPC 영역 — 방문자 얼굴 · 대화", 20, Soon);
            _boothCell = MakeCell(npc, 0.04f, 0.55f, 0.52f, 0.95f, Source.Booth, Field.Photo, null, Ui.Booth * 1.2f, Ui.Light, out _);
            _boothFace = Ui.Rect(_boothCell.Bg.transform, "Face", 0, 0, 1, 1, 6);
            _resultBox = Ui.Box(npc, "Result", 0.55f, 0.55f, 0.97f, 0.95f, Ui.Booth * 1.25f);
            _resultTitle = Ui.Label(_resultBox.transform, "Title", 0.05f, 0.62f, 0.95f, 0.95f, "", 40, Ui.Light, TextAnchor.MiddleCenter);
            _resultBody = Ui.Label(_resultBox.transform, "Body", 0.05f, 0.04f, 0.95f, 0.62f, "", 22, Ui.Light, TextAnchor.MiddleCenter, 4);

            Ui.Label(npc, "Who", 0.04f, 0.48f, 0.96f, 0.535f, "대화창 — 방문자의 첫마디", 20, new Color(0.7f, 0.7f, 0.7f));
            var bubble = Ui.Box(npc, "Bubble", 0.04f, 0.33f, 0.96f, 0.48f, Ui.Light).transform;
            _speech = Ui.Label(bubble, "Text", 0, 0, 1, 1, "", 30, Ui.Ink, TextAnchor.MiddleLeft, 20);

            // 심문 상호작용 자리 (대화 선택지 · 누락 서류 요청) — 로직 없음
            Ui.Label(npc, "AskZone", 0.04f, 0.27f, 0.96f, 0.32f, "심문 · 누락 서류 요청 (준비 중)", 20, Soon);
            string[] asks = { "나이 묻기", "서류 제출 요구", "심문 질문" };
            for (int i = 0; i < asks.Length; i++)
                Ui.Button(npc, "Ask" + i, 0.04f + i * 0.31f, 0.18f, 0.32f + i * 0.31f, 0.265f, asks[i] + "\n(준비 중)", Ui.Disabled, Soon, 20, null).interactable = false;
            _inspectLine = Ui.Label(npc, "Inspect", 0.04f, 0.01f, 0.96f, 0.17f, "", 24, Ui.Pick, TextAnchor.MiddleLeft);

            // ② 서류 검토 영역 (우측 상단): 임시 통행증 · 검역 소견서 (탭, 미제출이면 탭에 표시)
            var desk = Ui.Box(root, "DocArea", 0.405f, 0.455f, 0.8f, 0.95f, Ui.Desk).transform;
            for (int i = 0; i < DocTabNames.Length; i++)
            {
                int k = i;
                var b = Ui.Button(desk, "Tab" + i, 0.02f + i * 0.33f, 0.88f, 0.34f + i * 0.33f, 0.98f, DocTabNames[i], Ui.Solid(Ui.Paper * 0.8f), Ui.Ink, 22, () => { _tab = k; RebuildDoc(); Play(_tick); });
                _tabs.Add(b);
                _tabTexts.Add(b.GetComponentInChildren<Text>());
            }
            Ui.Label(desk, "Zone", 0.69f, 0.88f, 0.98f, 0.98f, "서류 검토", 20, Soon, TextAnchor.MiddleRight);
            _docContent = (RectTransform)Ui.Box(desk, "Paper", 0.02f, 0.02f, 0.98f, 0.87f, Ui.Paper).transform;

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
            _passBtn = Ui.Button(verdict, "Pass", 0.06f, 0.81f, 0.94f, 0.95f, "통과", Ui.PassGreen, Color.white, 40, () => Decide(Verdict.Pass));
            _denyBtn = Ui.Button(verdict, "Deny", 0.06f, 0.66f, 0.94f, 0.8f, "거부", Ui.DenyRed, Color.white, 40, () => Decide(Verdict.Deny));
            Ui.Button(verdict, "Report", 0.06f, 0.54f, 0.94f, 0.65f, "신고\n(준비 중)", Ui.Disabled, Soon, 24, null).interactable = false;
            Ui.Button(verdict, "Remove", 0.06f, 0.42f, 0.94f, 0.53f, "제거\n(준비 중)", Ui.Disabled, Soon, 24, null).interactable = false;
            var insp = Ui.Button(verdict, "Inspect", 0.06f, 0.22f, 0.94f, 0.36f, "", new Color(0.55f, 0.45f, 0.1f), Color.white, 24, ToggleInspect);
            _inspectBtnText = insp.GetComponentInChildren<Text>();
            _nextBtn = Ui.Button(verdict, "Next", 0.06f, 0.03f, 0.94f, 0.18f, "다음 방문자 ▶", new Color(0.25f, 0.35f, 0.55f), Color.white, 28, Next);
            SetInspect(false);
        }

        // 서류가 빠졌는지: 검역 소견서가 필요한 날인데 안 냈을 때
        bool CertMissing => V.Cert == null && Day.Has(ErrorKind.QuarantineMissing);

        void RebuildDoc()
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].image.color = i == _tab ? Ui.Paper : Ui.Solid(Ui.Paper * 0.7f);
                bool missing = i == 1 && CertMissing;
                _tabTexts[i].text = missing ? DocTabNames[i] + " (미제출)" : DocTabNames[i];
                _tabTexts[i].color = missing ? Ui.DenyRed : Ui.Ink;
            }
            _cells.RemoveAll(c => c.Src == Source.TravelPass || c.Src == Source.Quarantine);
            Ui.Clear(_docContent);
            var t = _docContent.transform;
            if (_tab == 0)
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
            MakeCell(t, x0, yTop - 0.11f, x1, yTop, src, f, value, Ui.Solid(Ui.Paper * 0.95f), Ui.Ink, out _);

        void RebuildTerminal()
        {
            for (int i = 0; i < _termTabs.Count; i++) _termTabs[i].image.color = i == _termTab ? Ui.Terminal * 4f : Ui.Terminal * 1.6f;
            _cells.RemoveAll(c => c.Src == Source.Database);
            Ui.Clear(_termContent);
            var t = _termContent.transform;
            if (_termTab == 1) { BuildManual(t); return; }
            var r = Record;
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
        FieldCell MakeCell(Transform parent, float x0, float y0, float x1, float y1, Source src, Field f, string value, Color bg, Color fg, out Text valueText)
        {
            var cell = new FieldCell { Src = src, Field = f, Normal = bg };
            var b = Ui.Button(parent, f.ToString(), x0, y0, x1, y1, "", bg, fg, 20, () => OnCellClicked(cell), 2);
            cell.Bg = b.image;
            var cb = b.colors; cb.normalColor = Color.white; b.colors = cb;
            Destroy(b.GetComponentInChildren<Text>().gameObject);
            valueText = null;
            if (value != null)
            {
                Ui.Label(b.transform, "Name", 0.02f, 0, 0.3f, 1, Fmt.FieldName(f), 21, Ui.Solid(Color.Lerp(fg, bg, 0.35f)), TextAnchor.MiddleLeft, 4);
                valueText = Ui.Label(b.transform, "Value", 0.3f, 0, 0.99f, 1, value, 26, fg, TextAnchor.MiddleLeft, 4);
            }
            if (src != Source.Booth) _cells.Add(cell);
            return cell;
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
            yield return Shot("day1_visitor");
            _termTab = 1; RebuildTerminal();
            yield return Shot("day1_manual");
            _termTab = 0; RebuildTerminal();
            bool shotMismatch = false, shotWrong = false;
            while (_overlay == null)
            {
                if (!shotMismatch && (V.Reason == ErrorKind.BirthMismatch || V.Reason == ErrorKind.AddressMismatch || V.Reason == ErrorKind.IdDigitMismatch))
                {
                    var f = V.Reason == ErrorKind.BirthMismatch ? Field.BirthDate : V.Reason == ErrorKind.AddressMismatch ? Field.Address : Field.ResidentId;
                    SetInspect(true);
                    OnCellClicked(_cells.First(c => c.Src == Source.TravelPass && c.Field == f));
                    OnCellClicked(_cells.First(c => c.Src == Source.Database && c.Field == f));
                    yield return Shot("day1_inspect_mismatch");
                    Decide(V.Correct);
                    yield return Shot("day1_result_correct");
                    shotMismatch = true;
                }
                else if (!shotWrong && V.Correct == Verdict.Pass && shotMismatch)
                {
                    Decide(Verdict.Deny);
                    yield return Shot("day1_result_wrong");
                    shotWrong = true;
                }
                else Decide(V.Correct);
                log.AppendLine($"D1 {V.Pass.Name} 심은 오류 {V.Planted} → 정답 {V.Correct} ({V.Reason})");
                Next();
                yield return null;
            }
            yield return Shot("day1_settlement");

            CloseOverlay(); _dayIndex = 1; StartDay();
            bool shotOutsider = false;
            while (_overlay == null)
            {
                if (!shotOutsider && V.Planted == ErrorKind.OutsiderAppearance)
                {
                    SetInspect(true);
                    OnCellClicked(_boothCell);
                    OnCellClicked(_cells.First(c => c.Src == Source.Database && c.Field == Field.Photo));
                    yield return Shot("day2_outsider_inspect");
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
            _tab = 1; RebuildDoc();
            yield return Shot("day3_cert_tab");
            foreach (var v in _visitors) log.AppendLine($"D3 {v.Pass.Name} 심은 오류 {v.Planted} → 정답 {v.Correct} ({v.Reason})");
            File.WriteAllText(Path.Combine(dir, "log.txt"), log.ToString());
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }

        // 사진에 필요한 장면(1일차 DB 불일치, 2일차 외부인)이 나오는 seed 찾기
        int PickShotSeed()
        {
            for (int s = 1; s < 500; s++)
            {
                var d1 = new VisitorGenerator(_db, s * 31 + 1).GenerateDay(_days[0]);
                var d2 = new VisitorGenerator(_db, s * 31 + 2).GenerateDay(_days[1]);
                bool mismatch = d1.Any(v => v.Reason is ErrorKind.BirthMismatch or ErrorKind.AddressMismatch or ErrorKind.IdDigitMismatch);
                bool cleanAfter = d1.SkipWhile(v => v.Reason is not (ErrorKind.BirthMismatch or ErrorKind.AddressMismatch or ErrorKind.IdDigitMismatch)).Skip(1).Any(v => v.Correct == Verdict.Pass);
                if (mismatch && cleanAfter && d2.Any(v => v.Planted == ErrorKind.OutsiderAppearance)) return s;
            }
            return 1;
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
