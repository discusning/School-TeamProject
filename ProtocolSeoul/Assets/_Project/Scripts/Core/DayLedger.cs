using System.Collections.Generic;
using System.Linq;

namespace ProtocolSeoul
{
    // 하루 정산 (보고서 P4). 숫자는 임시 — 기획서의 물자 배급·페널티가 정해지면 여기만 바꿈.
    public class DayLedger
    {
        public const int RationPerCorrect = 5;   // 맞게 처리한 1명당 배급 포인트
        public const int FreeWarnings = 2;       // 하루 실수 2번까지는 경고만 (페이퍼 플리즈 방식)
        public const int PenaltyPerMistake = 5;  // 3번째 실수부터 차감

        public class Entry
        {
            public Visitor Visitor;
            public Verdict Chosen;
            public bool Correct => Chosen == Visitor.Correct;
        }

        public readonly List<Entry> Entries = new();

        public void Record(Visitor v, Verdict chosen) => Entries.Add(new Entry { Visitor = v, Chosen = chosen });
        // 진00(대본 선택)은 실수로 세지 않음 — 플레이어의 선택으로 기록 (가정)
        static bool Scored(Entry e) => e.Visitor.Story != "jin";
        public int CorrectCount => Entries.Count(e => Scored(e) && e.Correct);
        public int Mistakes => Entries.Count(e => Scored(e) && !e.Correct);
        public int Earned => CorrectCount * RationPerCorrect;
        public int Penalty => System.Math.Max(0, Mistakes - FreeWarnings) * PenaltyPerMistake;

        // 실수 단계 (1주차 스토리 261122 프롤로그): 2번까진 경고, 3번부터 식량 감소, 4번부터 식량 미지급, 5번부터 퇴출(배드엔딩).
        // 하루 단위로 셈 · 감소량 = 3번째 실수부터 건당 PenaltyPerMistake (가정)
        public const int RationCutAt = 3, RationStopAt = 4, FiredAt = 5;
        public bool Fired => Mistakes >= FiredAt;
        public bool RationStopped => Mistakes >= RationStopAt;
        public int Total => RationStopped ? 0 : Earned - Penalty;
        public string Stage => Mistakes >= FiredAt ? "퇴출 (배드엔딩)" : Mistakes >= RationStopAt ? "식량 미지급" : Mistakes >= RationCutAt ? "식량 감소" : Mistakes > 0 ? "경고" : "실수 없음";
    }
}
