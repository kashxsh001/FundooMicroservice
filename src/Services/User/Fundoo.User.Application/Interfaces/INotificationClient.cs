using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fundoo.User.Application.Interfaces
{
    public interface INotificationClient
    {
        Task SendPasswordResetAsync(string email, string name, string resetToken,
            CancellationToken cancellationToken = default);
    }
}
