-- One database per service; each service applies its own EF Core migrations on startup.
CREATE DATABASE catalog;
CREATE DATABASE ordering;
CREATE DATABASE inventory;
CREATE DATABASE payment;
CREATE DATABASE identity;
