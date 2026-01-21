using System;
using System.ComponentModel.DataAnnotations;
using Jellyfin.Plugin.LanguageSelector.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LanguageSelector.Api;

[ApiController]
[Route("Items")]
[Authorize]
public class LanguageOptionsController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly MediaStreamAnalyzer _mediaStreamAnalyzer;
    private readonly ILogger<LanguageOptionsController> _logger;
    
    public LanguageOptionsController(
        ILibraryManager libraryManager,
        ILogger<LanguageOptionsController> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
        
        var languageDetector = new LanguageDetector();
        _mediaStreamAnalyzer = new MediaStreamAnalyzer(languageDetector);
    }
    
    [HttpGet("{itemId}/LanguageOptions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult GetLanguageOptions([FromRoute, Required] Guid itemId)
    {
        try
        {
            _logger.LogInformation("Fetching language options for item: {ItemId}", itemId);
            
            var item = _libraryManager.GetItemById(itemId);
            
            if (item == null)
            {
                _logger.LogWarning("Item not found: {ItemId}", itemId);
                return NotFound(new { error = "Item not found", itemId = itemId.ToString() });
            }
            
            var response = _mediaStreamAnalyzer.GetLanguageOptionsForItem(item);
            
            _logger.LogInformation(
                "Generated {OptionCount} language options for item: {ItemName}",
                response.Options.Count,
                item.Name);
            
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating language options for item: {ItemId}", itemId);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new { error = "Failed to generate language options", details = ex.Message });
        }
    }
}
