﻿using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using DocShareAPI.Data;
using DocShareAPI.DataTransferObject;
using DocShareAPI.Helpers;
using DocShareAPI.Helpers.PageList;
using DocShareAPI.Models;
using DocShareAPI.Services;
using ELearningAPI.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;

namespace DocShareAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DocumentsController : ControllerBase
    {
        private readonly DocShareDbContext _context;
        private readonly ILogger<DocumentsController> _logger;
        private readonly ICloudinaryService _cloudinaryService;
        private readonly long _maxFileSize;
        private readonly string[] _allowedDocumentTypes;

        public DocumentsController(DocShareDbContext context, ICloudinaryService cloudinaryService, ILogger<DocumentsController> logger, IConfiguration configuration)
        {
            _context = context;
            _cloudinaryService = cloudinaryService;
            _logger = logger;
            _maxFileSize = configuration.GetValue<long>("MaxFileSize", 10 * 1024 * 1024); // Default to 10MB
            _allowedDocumentTypes = configuration.GetSection("AllowedDocumentTypes").Get<string[]>() ?? new[] { "application/pdf", "application/msword", "text/plain" };
        }

        [HttpGet("documents")]
        public async Task<ActionResult> GetAllDocuments([FromQuery] PaginationParams paginationParams)
        {
            var query = _context.DOCUMENTS.AsQueryable();
            var pagedData = await query
                .Select(d => new
                {
                    d.document_id,
                    full_name = d.Users != null ? d.Users.full_name : string.Empty,
                    d.Title,
                    d.thumbnail_url,
                    d.is_public,
                    d.public_id
                })
                .ToPagedListAsync(paginationParams.PageNumber, paginationParams.PageSize);

            return Ok(new
            {
                Data = pagedData,
                Pagination = new
                {
                    pagedData.CurrentPage,
                    pagedData.PageSize,
                    pagedData.TotalCount,
                    pagedData.TotalPages
                }
            });
        }


        [HttpGet("document/{documentID}")]
        public async Task<ActionResult> GetDocumentByID(int documentID)
        {
            var decodedTokenResponse = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;

            var document = await _context.DOCUMENTS
                .Where(d => d.document_id == documentID)
                .Select(d => new
                {
                    d.document_id,
                    d.user_id,
                    d.Title,
                    d.Description,
                    d.file_url,
                    d.is_public,
                    d.download_count,
                    d.file_size,
                    d.file_type,
                    d.uploaded_at,
                    full_name = d.Users != null ? d.Users.full_name : string.Empty
                })
                .FirstOrDefaultAsync();

            if (document == null)
            {
                return NotFound(new { message = "Document not found." });
            }

            if (!document.is_public && (decodedTokenResponse == null || (decodedTokenResponse.userID != document.user_id && decodedTokenResponse.roleID != "admin")))
            {
                return Forbid("This is a private document!");
            }

            return Ok(document);
        }

        [HttpGet("my-uploaded-documents")]
        public async Task<ActionResult> GetMyUploadDocuments([FromQuery] PaginationParams paginationParams)
        {
            var decodedTokenResponse = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedTokenResponse == null)
            {
                return Unauthorized();
            }

            var query = _context.DOCUMENTS.AsQueryable();
            var pagedData = await query
                .Where(d => d.user_id == decodedTokenResponse.userID)
                .Select(d => new
                {
                    d.document_id,
                    d.Title,
                    d.Description,
                    d.thumbnail_url,
                    d.uploaded_at,
                    d.is_public
                })
                .OrderBy(d => d.uploaded_at)
                .ToPagedListAsync(paginationParams.PageNumber, paginationParams.PageSize);

            return Ok(new
            {
                Data = pagedData,
                Pagination = new
                {
                    pagedData.CurrentPage,
                    pagedData.TotalCount,
                    pagedData.TotalPages
                }
            });
        }

        [HttpPost("upload-document")]
        public async Task<ActionResult> UploadDocument(IFormFile file)
        {
            var decodedTokenResponse = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedTokenResponse == null)
            {
                return Unauthorized();
            }

            if (!IsValidDocument(file, out string validationMessage))
            {
                _logger.LogWarning(validationMessage);
                return BadRequest(validationMessage);
            }

            var user = await _context.USERS.FirstOrDefaultAsync(u => u.user_id == decodedTokenResponse.userID);
            if (user != null && !user.is_verified)
            {
                return Forbid("Upload failed! Please verify your account in the settings.");
            }

            var uploadResult = await UploadToCloudinary(file);
            if (uploadResult == null || uploadResult.Error != null)
            {
                var errorMessage = uploadResult?.Error?.Message ?? "Unknown error during upload";
                _logger.LogError($"Cloudinary upload failed: {errorMessage}");
                return StatusCode(500, $"Upload to Cloudinary failed: {errorMessage}");
            }

            var newDoc = await CreateDocumentRecord(file, decodedTokenResponse.userID, uploadResult);
            return Ok(new
            {
                message = "Document uploaded successfully",
                success = true,
                newDoc.document_id,
                newDoc.Title,
                newDoc.thumbnail_url,
                uploadResult
            });
        }

        [HttpPut("update-title-description")]
        public async Task<ActionResult> UpdateTitle(DocumentUpdateAfterUploadDTO documents)
        {
            var decodedTokenResponse = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedTokenResponse == null)
            {
                return Unauthorized();
            }

            var document = await _context.DOCUMENTS.FirstOrDefaultAsync(d => d.document_id == documents.document_id);
            if (document == null)
            {
                return BadRequest(new { message = "Document not found!" });
            }

            if (document.user_id != decodedTokenResponse.userID)
            {
                return BadRequest(new { message = "You are not the owner of this document" });
            }

            document.Title = documents.title;
            document.Description = documents.description;
            document.is_public = documents.is_public;
            _context.DOCUMENTS.Update(document);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Information saved successfully!",
                success = true,
                document.document_id
            });
        }

        [HttpDelete("delete-document")]
        public async Task<ActionResult> DeleteDocument(int documentID)
        {
            var decodedTokenResponse = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedTokenResponse == null)
            {
                return Unauthorized();
            }

            var document = await _context.DOCUMENTS.FirstOrDefaultAsync(d => d.document_id == documentID);
            if (document == null)
            {
                return BadRequest(new { message = "Document not found" });
            }

            if (document.user_id != decodedTokenResponse.userID && decodedTokenResponse.roleID != "admin")
            {
                return BadRequest(new { message = "You are not the owner of this document or an admin" });
            }

            var result = await DeleteFromCloudinary(document.public_id);
            if (result.Deleted != null && result.Deleted.ContainsKey(document.public_id))
            {
                _context.DOCUMENTS.Remove(document);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = $"Successfully deleted: {document.Title}",
                    success = true
                });
            }

            return BadRequest(new { message = "Failed to delete document from Cloudinary" });
        }

        [HttpGet("download-document/{documentID}")]
        public async Task<ActionResult> DownloadDocument(int documentID)
        {
            var decodedTokenResponse = HttpContext.Items["DecodedToken"] as DecodedTokenResponse;
            if (decodedTokenResponse == null)
            {
                return Unauthorized();
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var document = await _context.DOCUMENTS.FirstOrDefaultAsync(d => d.document_id == documentID);
                if (document == null)
                {
                    return NotFound($"No document found with ID: {documentID}");
                }

                document.download_count++;
                _context.DOCUMENTS.Update(document);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                var downloadURL = $"https://res-console.cloudinary.com/{_cloudinaryService.CloudName}/media_explorer_thumbnails/{document.asset_id}/download";
                return Ok(new
                {
                    success = true,
                    downloadURL
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return BadRequest(ex.Message);
            }
        }

        private bool IsValidDocument(IFormFile file, out string validationMessage)
        {
            validationMessage = string.Empty;

            if (file == null || file.Length == 0)
            {
                validationMessage = "Please select a document to upload.";
                return false;
            }

            if (file.Length > _maxFileSize)
            {
                validationMessage = $"Document size {file.Length} exceeds maximum allowed size of {_maxFileSize / 1024 / 1024}MB.";
                return false;
            }

            if (!_allowedDocumentTypes.Contains(file.ContentType))
            {
                validationMessage = $"Invalid document type: {file.ContentType}. Allowed types are: PDF, DOCX, TXT.";
                return false;
            }

            return true;
        }

        private async Task<ImageUploadResult> UploadToCloudinary(IFormFile file)
        {
            string folder = "DocShare/Documents";
            using var stream = file.OpenReadStream();
            var uploadParams = new ImageUploadParams()
            {
                File = new FileDescription(file.FileName, stream),
                Folder = folder,
                UseFilename = true,
                UniqueFilename = true,
                Overwrite = false,
                Tags = file.ContentType
            };

            return await _cloudinaryService.Cloudinary.UploadAsync(uploadParams);
        }

        private async Task<Documents> CreateDocumentRecord(IFormFile file, Guid userId, ImageUploadResult uploadResult)
        {
            var newID = GenerateRandomCode.GenerateID();
            while (await _context.DOCUMENTS.AnyAsync(d => d.document_id == newID))
            {
                newID = GenerateRandomCode.GenerateID();
            }

            var newDoc = new Documents
            {
                document_id = newID,
                user_id = userId,
                Title = $"{ConvertPdf.ConvertPdfTitle(file.FileName)}-{newID}",
                file_url = uploadResult.SecureUrl.ToString(),
                public_id = uploadResult.PublicId,
                asset_id = uploadResult.AssetId,
                thumbnail_url = ConvertPdf.ConvertPdfTitleToJpg(uploadResult.SecureUrl.ToString()),
                file_size = Convert.ToInt32(file.Length),
                file_type = uploadResult.Format,
                pages = uploadResult.Pages,
                uploaded_at = DateTime.UtcNow
            };

            _context.DOCUMENTS.Add(newDoc);
            await _context.SaveChangesAsync();
            return newDoc;
        }

        private async Task<DelResResult> DeleteFromCloudinary(string publicId)
        {
            var deleteParams = new DelResParams
            {
                PublicIds = new List<string> { publicId },
                Type = "upload",
                ResourceType = ResourceType.Image
            };

            return await _cloudinaryService.Cloudinary.DeleteResourcesAsync(deleteParams);
        }
    }
}
