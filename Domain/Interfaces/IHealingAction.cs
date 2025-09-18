using Domain.Models;
using Models;

namespace Domain.Interfaces
{
    public interface IHealingAction
    {
        /// <summary> Tên use-case, chỉ để log. </summary>
        string Name { get; }

        /// <summary> Trả true nếu action này cần chạy. </summary>
        bool ShouldRun(ValidationResult v, ProcessingLog log);

        /// <summary> Thực thi. </summary>
        Task RunAsync(string parentFolder, WorkDirs work, ProcessingLog log);
    }
}
