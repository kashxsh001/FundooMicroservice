using Fundoo.Notes.Api.Contracts.Requests;
using Fundoo.Notes.Api.Contracts.Response;
using Fundoo.Notes.Application.Interfaces;
using Fundoo.Notes.Domain.Entity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Fundoo.Notes.Api.Controllers
{
    [ApiController]
    [Route("api/notes")]
    [Authorize]
    public class NotesController : ControllerBase
    {
        private readonly INotesService _notesService;
        private readonly ILabelsService _labelsService;

        public NotesController(INotesService notesService, ILabelsService labelsService)
        {
            _notesService = notesService;
            _labelsService = labelsService;
        }

        [HttpGet]
        public async Task<IActionResult> GetActiveNotes(CancellationToken cancellationToken)
        {
            if (!TryGetUserId(out var userId))return Unauthorized(new { message = "Invalid user token." });

            var notes = await _notesService.GetActiveAsync(userId, cancellationToken);

            return Ok(new
            {
                isSuccess = true,
                message = "Active notes retrieved successfully.",
                data = notes.Select(MapToResponse).ToList()
            });
        }

        [HttpGet("archived")]
        public async Task<IActionResult> GetArchivedNotes(CancellationToken cancellationToken)
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Invalid user token." });

            var notes = await _notesService.GetArchivedAsync(userId, cancellationToken);

            return Ok(new
            {
                isSuccess = true,
                message = "Archived notes retrieved successfully.",
                data = notes.Select(MapToResponse).ToList()
            });
        }

        [HttpGet("trash")]
        public async Task<IActionResult> GetTrashedNotes(CancellationToken cancellationToken)
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Invalid user token." });

            var notes = await _notesService.GetTrashedAsync(userId, cancellationToken);

            return Ok(new
            {
                isSuccess = true,
                message = "Trashed notes retrieved successfully.",
                data = notes.Select(MapToResponse).ToList()
            });
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetNoteById(Guid id, CancellationToken cancellationToken)
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Invalid user token." });

            var note = await _notesService.GetByIdAsync(id, userId, cancellationToken);
            if (note is null)
                return NotFound(new { message = "Note not found." });

            return Ok(new
            {
                isSuccess = true,
                message = "Note retrieved successfully.",
                data = MapToResponse(note)
            });
        }

        [HttpPost]
        public async Task<IActionResult> CreateNote([FromBody] CreateNoteRequest request,
            CancellationToken cancellationToken)
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Invalid user token." });

            var note = new Note
            {
                UserId = userId,
                Title = request.Title.Trim(),
                Content = request.Content ?? string.Empty,
                Color = request.Color,
                IsPinned = request.IsPinned
            };

            try
            {
                var created = await _notesService.CreateAsync(note, cancellationToken);

                return CreatedAtAction(
                    nameof(GetNoteById),
                    new { id = created.Id },
                    new
                    {
                        isSuccess = true,
                        message = "Note created successfully.",
                        data = MapToResponse(created)
                    });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> UpdateNote(Guid id, [FromBody] UpdateNoteRequest request,
            CancellationToken cancellationToken)
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Invalid user token." });

            var note = new Note
            {
                Id = id,
                UserId = userId,
                Title = request.Title.Trim(),
                Content = request.Content ?? string.Empty,
                Color = request.Color,
                IsPinned = request.IsPinned
            };

            try
            {
                var updated = await _notesService.UpdateAsync(note, cancellationToken);
                if (!updated)
                    return NotFound(new { message = "Note not found or cannot be updated." });

                return Ok(new { isSuccess = true, message = "Note updated successfully." });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("{id:guid}/archive")]
        public Task<IActionResult> ArchiveNote(Guid id, CancellationToken cancellationToken)
            => ChangeArchiveStatus(id, true, cancellationToken);

        [HttpPut("{id:guid}/unarchive")]
        public Task<IActionResult> UnarchiveNote(Guid id, CancellationToken cancellationToken)
            => ChangeArchiveStatus(id, false, cancellationToken);

        [HttpPut("{id:guid}/trash")]
        public Task<IActionResult> TrashNote(Guid id, CancellationToken cancellationToken)
            => ChangeTrashStatus(id, true, cancellationToken);

        [HttpPut("{id:guid}/restore")]
        public Task<IActionResult> RestoreNote(Guid id, CancellationToken cancellationToken)
            => ChangeTrashStatus(id, false, cancellationToken);

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> DeleteNote(Guid id, CancellationToken cancellationToken)
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Invalid user token." });

            var deleted = await _notesService.DeleteAsync(id, userId, cancellationToken);
            if (!deleted)
                return NotFound(new { message = "Note not found or must be moved to trash before deletion." });

            return Ok(new { isSuccess = true, message = "Note permanently deleted." });
        }

        [HttpPut("{noteId:guid}/labels/{labelId:guid}")]
        public async Task<IActionResult> AttachLabel(Guid noteId, Guid labelId, CancellationToken cancellationToken)
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Invalid user token." });

            var attached = await _labelsService.AttachToNoteAsync(noteId, labelId, userId, cancellationToken);
            if (!attached)
                return NotFound(new { message = "Note or label not found." });

            return Ok(new { isSuccess = true, message = "Label attached to note successfully." });
        }

        [HttpDelete("{noteId:guid}/labels/{labelId:guid}")]
        public async Task<IActionResult> DetachLabel(Guid noteId, Guid labelId, CancellationToken cancellationToken)
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Invalid user token." });

            var detached = await _labelsService.DetachFromNoteAsync(noteId, labelId, userId, cancellationToken);
            if (!detached)
                return NotFound(new { message = "Note-label association not found." });

            return Ok(new { isSuccess = true, message = "Label removed from note successfully." });
        }

        private async Task<IActionResult> ChangeArchiveStatus(Guid id, bool isArchived,
            CancellationToken cancellationToken)
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Invalid user token." });

            var updated = await _notesService.SetArchivedAsync(id, userId, isArchived, cancellationToken);
            if (!updated)
                return NotFound(new { message = "Note not found or cannot be archived." });

            return Ok(new
            {
                isSuccess = true,
                message = isArchived ? "Note archived successfully." : "Note unarchived successfully."
            });
        }

        private async Task<IActionResult> ChangeTrashStatus(Guid id, bool isTrashed,
            CancellationToken cancellationToken)
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Invalid user token." });

            var updated = await _notesService.SetTrashedAsync(id, userId, isTrashed, cancellationToken);
            if (!updated)
                return NotFound(new { message = "Note not found." });

            return Ok(new
            {
                isSuccess = true,
                message = isTrashed ? "Note moved to trash successfully." : "Note restored successfully."
            });
        }

        // Reads the user id ("sub") from the JWT.
        private bool TryGetUserId(out Guid userId)
        {
            var claim = User.FindFirst("sub") ?? User.FindFirst(ClaimTypes.NameIdentifier);
            return Guid.TryParse(claim?.Value, out userId);
        }

        private static NoteResponse MapToResponse(Note note) => new NoteResponse
        {
            Id = note.Id,
            UserId = note.UserId,
            Title = note.Title,
            Content = note.Content,
            Color = note.Color,
            IsPinned = note.IsPinned,
            IsArchived = note.IsArchived,
            IsTrashed = note.IsTrashed,
            CreatedAtUtc = note.CreatedAtUtc,
            UpdatedAtUtc = note.UpdatedAtUtc,
            Labels = note.NoteLabels
                .Where(nl => nl.Label != null)
                .Select(nl => new NoteLabelResponse { Id = nl.Label.Id, Name = nl.Label.Name })
                .ToList()
        };
    }
}