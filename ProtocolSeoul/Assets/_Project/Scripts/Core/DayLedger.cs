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
        public int CorrectCount => Entries.Count(e => e.Correct);
        public int Mistakes => Entries.Count(e => !e.Correct);
        public int Earned => CorrectCount * RationPerCorrect;
        public int Penalty => System.Math.Max(0, Mistakes - FreeWarnings) * PenaltyPerMistake;
        public int Total => Earned - Penalty;
    }
}
