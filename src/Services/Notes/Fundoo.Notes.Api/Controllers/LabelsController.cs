using Fundoo.Notes.Api.Contracts.Requests;
using Fundoo.Notes.Api.Contracts.Response;
using Fundoo.Notes.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Fundoo.Notes.Api.Controllers
{
    [ApiController]
    [Route("api/labels")]
    [Authorize]
    public class LabelsController : ControllerBase
    {
        private readonly ILabelsService _labelsService;

        public LabelsController(ILabelsService labelsService) => _labelsService = labelsService;

        [HttpPost]
        public async Task<IActionResult> CreateLabel([FromBody] CreateLabelRequest request,
            CancellationToken cancellationToken)
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Invalid user token." });

            try
            {
                var label = await _labelsService.CreateAsync(userId, request.Name, cancellationToken);

                return StatusCode(StatusCodes.Status201Created, new
                {
                    isSuccess = true,
                    message = "Label created successfully.",
                    data = new NoteLabelResponse { Id = label.Id, Name = label.Name }
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetLabels(CancellationToken cancellationToken)
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Invalid user token." });

            var labels = await _labelsService.GetAllAsync(userId, cancellationToken);

            return Ok(new
            {
                isSuccess = true,
                message = "Labels retrieved successfully.",
                data = labels.Select(l => new NoteLabelResponse { Id = l.Id, Name = l.Name }).ToList()
            });
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> UpdateLabel(Guid id, [FromBody] UpdateLabelRequest request,
            CancellationToken cancellationToken)
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Invalid user token." });

            try
            {
                var updated = await _labelsService.UpdateAsync(id, userId, request.Name, cancellationToken);
                if (!updated)
                    return NotFound(new { message = "Label not found." });

                return Ok(new { isSuccess = true, message = "Label updated successfully." });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> DeleteLabel(Guid id, CancellationToken cancellationToken)
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Invalid user token." });

            var deleted = await _labelsService.DeleteAsync(id, userId, cancellationToken);
            if (!deleted)
                return NotFound(new { message = "Label not found." });

            return Ok(new { isSuccess = true, message = "Label deleted successfully." });
        }

        private bool TryGetUserId(out Guid userId)
        {
            var claim = User.FindFirst("sub") ?? User.FindFirst(ClaimTypes.NameIdentifier);
            return Guid.TryParse(claim?.Value, out userId);
        }
    }
}