## Slack Notification Alert System

The Slack Notification Alert System was a RESTful API centralized service used by all internal applications to send detailed error notifications in the event of unhandled exceptions. This application provided real-time alerts to Slack channels, enabling rapid debugging and troubleshooting of remote application errors. In the event of a Slack API failure, the service seamlessly switched to email notifications, ensuring critical error messages were always delivered. This redundancy improved reliability and minimized downtime during incidents.

### Technologies Used
- C#, .NET, RESTful API
- SQL Server for logging and tracking - notifications
- Slack API for real-time notifications
- Email integration as a fallback mechanism
