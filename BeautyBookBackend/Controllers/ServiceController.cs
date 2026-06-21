using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Services;
using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.Controllers
{
    [ApiController]
    [Route("api/Mua")]
    public class ServiceController : ControllerBase
    {
        private readonly IMuaService _muaService;

        public ServiceController(IMuaService muaService)
        {
            _muaService = muaService;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? Guid.Empty.ToString());

        [HttpGet("{muaId}/service")]
        public async Task<IActionResult> GetServicesByMua(Guid muaId)
        {
            var services = await _muaService.GetMuaServicesAsync(muaId);
            return Ok(services);
        }

        [Authorize]
        [HttpPost("{muaId}/service")]
        public async Task<IActionResult> AddService(string muaId, [FromBody] ServiceCreateDto serviceDto)
        {
            if (!await _muaService.HasMuaProfileAsync(CurrentUserId))
            {
                return Forbid();
            }

            var service = await _muaService.AddMuaServiceAsync(CurrentUserId, serviceDto);
            if (service == null)
            {
                return BadRequest(new { Message = "ThÃªm dá»‹ch vá»¥ tháº¥t báº¡i. Äáº£m báº£o há»“ sÆ¡ Makeup Artist cá»§a báº¡n Ä‘Ã£ Ä‘Æ°á»£c thiáº¿t láº­p." });
            }

            return Ok(new { Message = "ÄÃ£ thÃªm gÃ³i dá»‹ch vá»¥ trang Ä‘iá»ƒm má»›i thÃ nh cÃ´ng!", Service = service });
        }

        [Authorize]
        [HttpPut("service/{id}")]
        public async Task<IActionResult> UpdateService(Guid id, [FromBody] ServiceCreateDto serviceDto)
        {
            if (!await _muaService.HasMuaProfileAsync(CurrentUserId))
            {
                return Forbid();
            }

            var success = await _muaService.UpdateMuaServiceAsync(CurrentUserId, id, serviceDto);
            if (!success)
            {
                return NotFound(new { Message = "KhÃ´ng tÃ¬m tháº¥y gÃ³i dá»‹ch vá»¥ nÃ y hoáº·c báº¡n khÃ´ng cÃ³ quyá»n chá»‰nh sá»­a nÃ³." });
            }

            return Ok(new { Message = "Cáº­p nháº­t dá»‹ch vá»¥ thÃ nh cÃ´ng!" });
        }

        [Authorize]
        [HttpDelete("service/{id}")]
        public async Task<IActionResult> DeleteService(Guid id)
        {
            if (!await _muaService.HasMuaProfileAsync(CurrentUserId))
            {
                return Forbid();
            }

            var success = await _muaService.DeleteMuaServiceAsync(CurrentUserId, id);
            if (!success)
            {
                return NotFound(new { Message = "KhÃ´ng tÃ¬m tháº¥y gÃ³i dá»‹ch vá»¥ nÃ y hoáº·c báº¡n khÃ´ng cÃ³ quyá»n xÃ³a." });
            }

            return Ok(new { Message = "ÄÃ£ xÃ³a gÃ³i dá»‹ch vá»¥ trang Ä‘iá»ƒm thÃ nh cÃ´ng." });
        }
    }
}

