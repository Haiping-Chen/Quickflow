﻿using Amazon.Runtime;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using EntityFrameworkCore.BootKit;
using Microsoft.Extensions.Configuration;
using Quickflow.Core;
using Quickflow.Core.Entities;
using Quickflow.Core.Interfacess;
using RazorLight;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Quickflow.ActivityRepository
{
    public class EmailInAwsActivity : EssentialActivity, IWorkflowActivity
    {
        public async Task Run(Database dc, Workflow wf, ActivityInWorkflow activity, ActivityInWorkflow preActivity)
        {
            var configuration = (IConfiguration)AppDomain.CurrentDomain.GetData("Configuration");
            EmailRequestModel model = new EmailRequestModel();

            model.Subject = activity.GetOptionValue("Subject");
            model.ToAddresses = activity.GetOptionValue("ToAddresses");
            model.Body = activity.GetOptionValue("Body");
            model.Template = activity.GetOptionValue("Template");
            model.Bcc = activity.GetOptionValue("Bcc");
            model.Cc = activity.GetOptionValue("Cc");

            if (!String.IsNullOrEmpty(model.Template))
            {
                var engine = new RazorLightEngineBuilder()
                  .UseFilesystemProject(AppDomain.CurrentDomain.GetData("ContentRootPath").ToString() + "\\App_Data")
                  .UseMemoryCachingProvider()
                  .Build();

                model.Body = await engine.CompileRenderAsync(model.Template, activity.Input.Data);
            }

            var ses = new SesEmailConfig
            {
                VerifiedEmail = configuration.GetSection("AWS:SESVerifiedEmail").Value,
                AWSSecretKey = configuration.GetSection("AWS:AWSSecretKey").Value,
                AWSAccessKey = configuration.GetSection("AWS:AWSAccessKey").Value
            };

            activity.Output.Data = await Send(model, ses);
        }

        private SendEmailRequest PrepareEmailRequest(EmailRequestModel model, String from)
        {
            // Construct an object to contain the recipient address.
            Destination destination = new Destination();
            destination.ToAddresses = model.ToAddresses.Split(',').Select(x => x.Trim()).ToList();
            if (!String.IsNullOrEmpty(model.Bcc))
            {
                destination.BccAddresses = model.Bcc.Split(',').Select(x => x.Trim()).ToList();
            }

            if (!String.IsNullOrEmpty(model.Cc))
            {
                destination.CcAddresses = model.Cc.Split(',').Select(x => x.Trim()).ToList();
            }

            // Create the subject and body of the message.
            Content subject = new Content(model.Subject);

            Body body = new Body();
            body.Html = new Content(model.Body);

            // Create a message with the specified subject and body.
            Message message = new Message(subject, body);

            // Assemble the email.
            return new SendEmailRequest(from, destination, message);
        }

        private AmazonSimpleEmailServiceClient PrepareEmailClient(EmailRequestModel model, SesEmailConfig config)
        {
            // Choose the AWS region of the Amazon SES endpoint you want to connect to. Note that your sandbox 
            // status, sending limits, and Amazon SES identity-related settings are specific to a given 
            // AWS region, so be sure to select an AWS region in which you set up Amazon SES. Here, we are using 
            // the US West (Oregon) region. Examples of other regions that Amazon SES supports are USEast1 
            // and EUWest1. For a complete list, see http://docs.aws.amazon.com/ses/latest/DeveloperGuide/regions.html 
            Amazon.RegionEndpoint REGION = Amazon.RegionEndpoint.USEast1;

            // Instantiate an Amazon SES client, which will make the service call.
            AmazonSimpleEmailServiceClient client = new AmazonSimpleEmailServiceClient(config.AWSAccessKey, config.AWSSecretKey, REGION);

            client.BeforeRequestEvent += delegate (object sender, RequestEventArgs e)
            {
                WebServiceRequestEventArgs args = e as WebServiceRequestEventArgs;
                SendEmailRequest request = args.Request as SendEmailRequest;

                //$"Sending email {model.Subject} to {model.ToAddresses}".Log();
            };

            client.ExceptionEvent += delegate (object sender, ExceptionEventArgs e)
            {
                Console.WriteLine($"Sent email {model.Subject} error: {e.ToString()}");
            };

            client.AfterResponseEvent += delegate (object sender, ResponseEventArgs e)
            {
                WebServiceResponseEventArgs args = e as WebServiceResponseEventArgs;
                SendEmailResponse response = args.Response as SendEmailResponse;

                //$"Sent email {model.Subject} to {model.ToAddresses} {response.HttpStatusCode} {response.MessageId}".Log();
            };

            return client;
        }

        private void Client_BeforeRequestEvent(object sender, RequestEventArgs e)
        {
            throw new NotImplementedException();
        }

        private async Task<string> Send(EmailRequestModel model, SesEmailConfig config)
        {
            if (String.IsNullOrEmpty(model.ToAddresses)) return String.Empty;

            SendEmailRequest request = PrepareEmailRequest(model, config.VerifiedEmail);
            model.From = request.Source;

            AmazonSimpleEmailServiceClient client = PrepareEmailClient(model, config);

            SendEmailResponse response = await client.SendEmailAsync(request);
            return response.MessageId;
        }

        private class EmailRequestModel
        {
            public string Subject { get; set; }
            /// <summary>
            /// Support mutiple addresses seperated by comma
            /// </summary>
            public string ToAddresses { get; set; }
            public string Body { get; set; }
            public string From { get; set; }
            public string Bcc { get; set; }
            public string Cc { get; set; }
            /// <summary>
            /// Template file name
            /// </summary>
            public string Template { get; set; }
        }

        private class SesEmailConfig
        {
            public String VerifiedEmail { get; set; }
            public String AWSSecretKey { get; set; }
            public String AWSAccessKey { get; set; }
        }
    }
}
