-- ApplicationUsers
INSERT INTO AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount)
VALUES
    ('1', 'user1@example.com', 'USER1@EXAMPLE.COM', 'user1@example.com', 'USER1@EXAMPLE.COM', 1, 'hashedpassword1', 'securitystamp1', 'concurrencystamp1', '1234567890', 1, 0, NULL, 0, 0),
    ('2', 'user2@example.com', 'USER2@EXAMPLE.COM', 'user2@example.com', 'USER2@EXAMPLE.COM', 1, 'hashedpassword2', 'securitystamp2', 'concurrencystamp2', '0987654321', 1, 0, NULL, 0, 0);

-- ApplicationRoles
INSERT INTO AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
VALUES
    ('1', 'Admin', 'ADMIN', 'rolestamp1'),
    ('2', 'User', 'USER', 'rolestamp2');

-- Groups
INSERT INTO Groups (Name, AdminId)
VALUES
    ('Group1', '1'),
    ('Group2', '2');

-- Categories
INSERT INTO Categories (Name)
VALUES
    ('Work'),
    ('Entertainment'),
    ('Education');

-- Devices
INSERT INTO Devices (Name, UserId)
VALUES
    ('Laptop', 1),
    ('Smartphone', 2);

-- GroupHasUsers
INSERT INTO GroupHasUsers (UserId, GroupId)
VALUES
    ('1', 1),
    ('2', 1),
    ('2', 2);

-- RegisteredApps
INSERT INTO RegisteredApps (Name)
VALUES
    ('Microsoft Office'),
    ('Google Chrome'),
    ('Zoom');

-- Usage
INSERT INTO Usage (Id, Date, Duration, DeviceId, CategoryId, AppId)
VALUES
    ('1', '2025-03-25', '02:30:00', 1, 1, 1),
    ('2', '2025-03-25', '01:45:00', 2, 2, 2),
    ('3', '2025-03-25', '00:45:00', 1, 3, 3);
