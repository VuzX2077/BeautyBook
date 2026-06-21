import re

with open('D:/EXE/BeautyBook/BeautyBookBackend/Services/BookingService.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# 1. Update CreateBookingAsync
create_async_old = '''        public async Task<BookingDto?> CreateBookingAsync(Guid customerId, BookingCreateDto createDto)
        {
            var service = await _muaRepository.GetServiceByIdForMuaAsync(createDto.ServiceId, createDto.MUAId);
            if (service == null) return null;

            var customerWallet = await _walletRepository.GetByUserIdAsync(customerId);

            var booking = new Booking
            {
                BookingId = Guid.NewGuid(),
                CustomerId = customerId,
                MUAId = createDto.MUAId,
                ServiceId = createDto.ServiceId,
                StartTime = createDto.StartTime,
                EndTime = createDto.EndTime,
                Status = BookingStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            await _bookingRepository.AddAsync(booking);

            if (customerWallet != null)
            {
                customerWallet.Balance -= service.Price;
                customerWallet.UpdatedAt = DateTime.UtcNow;

                await _walletRepository.AddTransactionAsync(new WalletTransaction
                {
                    TransactionId = Guid.NewGuid(),
                    WalletId = customerWallet.WalletId,
                    Amount = -service.Price,
                    TransactionType = TransactionType.BookingPayment,
                    Description = $"Thanh toan dat lich #{booking.BookingId.ToString().Substring(0, 8)}",
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _unitOfWork.SaveChangesAsync();
            return await ToBookingDtoAsync(booking);
        }'''

create_async_new = '''        public async Task<BookingDto?> CreateBookingAsync(Guid customerId, BookingCreateDto createDto)
        {
            if (createDto.Services == null || !createDto.Services.Any()) return null;

            decimal totalAmount = 0;
            int totalDuration = 0;
            var bookingServices = new List<BookingService>();
            
            var bookingId = Guid.NewGuid();

            foreach (var s in createDto.Services)
            {
                var service = await _muaRepository.GetServiceByIdForMuaAsync(s.ServiceId, createDto.MUAId);
                if (service == null) return null;
                
                var price = service.Price * s.ParticipantsCount;
                var duration = service.DurationMinutes * s.ParticipantsCount;
                
                totalAmount += price;
                totalDuration += duration;
                
                bookingServices.Add(new BookingService
                {
                    Id = Guid.NewGuid(),
                    BookingId = bookingId,
                    ServiceId = service.ServiceId,
                    ServiceName = service.ServiceName ?? "",
                    PriceSnapshot = service.Price,
                    DurationMinutesSnapshot = service.DurationMinutes,
                    ParticipantsCount = s.ParticipantsCount
                });
            }

            var customerWallet = await _walletRepository.GetByUserIdAsync(customerId);

            // Need to calculate EndTime based on StartTime and TotalDuration
            // But createDto.StartTime is already TimeSpan
            var endTime = createDto.StartTime.Add(TimeSpan.FromMinutes(totalDuration));

            var booking = new Booking
            {
                BookingId = bookingId,
                CustomerId = customerId,
                MUAId = createDto.MUAId,
                TotalAmount = totalAmount,
                TotalDurationMinutes = totalDuration,
                BookingDate = createDto.BookingDate,
                StartTime = createDto.StartTime,
                EndTime = endTime,
                Address = createDto.Address,
                Notes = createDto.Notes,
                Status = BookingStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                BookingServices = bookingServices
            };

            await _bookingRepository.AddAsync(booking);

            if (customerWallet != null)
            {
                customerWallet.Balance -= totalAmount;
                customerWallet.UpdatedAt = DateTime.UtcNow;

                await _walletRepository.AddTransactionAsync(new WalletTransaction
                {
                    TransactionId = Guid.NewGuid(),
                    WalletId = customerWallet.WalletId,
                    Amount = -totalAmount,
                    TransactionType = TransactionType.BookingPayment,
                    Description = $"Thanh toan dat lich #{booking.BookingId.ToString().Substring(0, 8)}",
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _unitOfWork.SaveChangesAsync();
            return await ToBookingDtoAsync(booking);
        }'''

content = content.replace(create_async_old, create_async_new)

# 2. CompleteBookingAsync
content = content.replace("var servicePrice = booking.Service?.Price ?? 0;", "var servicePrice = booking.TotalAmount;")

# 3. RefundBookingAsync
content = content.replace("var servicePrice = booking.Service?.Price ?? 0;", "var servicePrice = booking.TotalAmount;")

# 4. ToBookingDtoAsync
to_booking_old = '''        private async Task<BookingDto> ToBookingDtoAsync(Booking booking)
        {
            return new BookingDto
            {
                BookingId = booking.BookingId,
                CustomerId = booking.CustomerId,
                CustomerName = booking.Customer?.FullName,
                MUAId = booking.MUAId,
                MuaName = booking.MakeupArtistProfile?.User?.FullName,
                ServiceId = booking.ServiceId,
                ServiceName = booking.Service?.ServiceName,
                StartTime = booking.StartTime,
                EndTime = booking.EndTime,
                Status = booking.Status,
                HasReview = await _reviewRepository.ExistsForBookingAsync(booking.BookingId),
                CreatedAt = booking.CreatedAt
            };
        }'''

to_booking_new = '''        private async Task<BookingDto> ToBookingDtoAsync(Booking booking)
        {
            var dto = new BookingDto
            {
                BookingId = booking.BookingId,
                CustomerId = booking.CustomerId,
                CustomerName = booking.Customer?.FullName,
                MUAId = booking.MUAId,
                MuaName = booking.MakeupArtistProfile?.User?.FullName,
                TotalAmount = booking.TotalAmount,
                TotalDurationMinutes = booking.TotalDurationMinutes,
                BookingDate = booking.BookingDate,
                StartTime = booking.StartTime,
                EndTime = booking.EndTime,
                Address = booking.Address,
                Notes = booking.Notes,
                Status = booking.Status,
                HasReview = await _reviewRepository.ExistsForBookingAsync(booking.BookingId),
                CreatedAt = booking.CreatedAt,
                Services = new List<BookingServiceDto>()
            };
            
            if (booking.BookingServices != null)
            {
                foreach(var bs in booking.BookingServices)
                {
                    dto.Services.Add(new BookingServiceDto
                    {
                        ServiceId = bs.ServiceId,
                        ServiceName = bs.ServiceName,
                        Price = bs.PriceSnapshot,
                        ParticipantsCount = bs.ParticipantsCount,
                        DurationMinutes = bs.DurationMinutesSnapshot
                    });
                }
            }
            return dto;
        }'''

content = content.replace(to_booking_old, to_booking_new)

with open('D:/EXE/BeautyBook/BeautyBookBackend/Services/BookingService.cs', 'w', encoding='utf-8') as f:
    f.write(content)

print("Updated BookingService.cs")
