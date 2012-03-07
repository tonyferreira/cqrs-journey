﻿// ==============================================================================================================
// Microsoft patterns & practices
// CQRS Journey project
// ==============================================================================================================
// Copyright (c) Microsoft Corporation and contributors http://cqrsjourney.github.com/contributors/members
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance 
// with the License. You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is 
// distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. 
// See the License for the specific language governing permissions and limitations under the License.
// ==============================================================================================================

namespace Conference.Web.Public.Controllers
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Web.Mvc;
    using Common;
    using Conference.Web.Public.Models;
    using Registration.Commands;
    using Registration.ReadModel;

    public class RegistrationController : Controller
    {
        private const int WaitTimeoutInSeconds = 5;

        private ICommandBus commandBus;
        private Func<IViewRepository> repositoryFactory;

        public RegistrationController()
            : this(MvcApplication.GetService<ICommandBus>(), MvcApplication.GetService<Func<IViewRepository>>())
        {
        }

        public RegistrationController(ICommandBus commandBus, Func<IViewRepository> repositoryFactory)
        {
            this.commandBus = commandBus;
            this.repositoryFactory = repositoryFactory;
        }

        [HttpGet]
        public ActionResult ChooseSeats(string conferenceName)
        {
            var registration = CreateRegistration(conferenceName);
            registration.Id = Guid.NewGuid();

            return View(registration);
        }

        [HttpPost]
        public ActionResult ChoosePayment(string conferenceName, Registration contentModel)
        {
            var registration = UpdateRegistration(conferenceName, contentModel);

            var command =
                new RegisterToConference
                {
                    RegistrationId = registration.Id,
                    ConferenceId = registration.ConferenceId,
                    Tickets = registration.Seats.Select(x => new RegisterToConference.Ticket { TicketTypeId = x.SeatId, Quantity = x.Quantity }).ToList()
                };

            this.commandBus.Send(command);

            var orderDTO = this.WaitUntilBooked(registration);

            if (orderDTO != null)
            {
                if (orderDTO.State == "Booked")
                {
                    return View(registration);
                }
                else if (orderDTO.State == "Rejected")
                {
                    return View("ReservationRejected", registration);
                }
            }

            return View("ReservationUnknown", registration);

        }

        [HttpPost]
        public ActionResult ConfirmRegistration(string conferenceName, Registration contentModel)
        {
            var registration = contentModel;

            var command =
                new SetRegistrationPaymentDetails
                {
                    RegistrationId = registration.Id,
                    PaymentInformation = "payment"
                };

            this.commandBus.Send(command);

            return View("RegistrationConfirmed");
        }

        private Registration CreateRegistration(string conferenceName)
        {
            var registration =
                new Registration
                {
                    ConferenceName = conferenceName,
                    Seats = { new Seat { SeatId = "testSeat", SeatDescription = "Test seat", Price = 100f } }
                };

            return registration;
        }

        private Registration UpdateRegistration(string conferenceName, Registration contentModel)
        {
            var reservation = this.CreateRegistration(conferenceName);
            reservation.Id = contentModel.Id;

            for (int i = 0; i < reservation.Seats.Count; i++)
            {
                var quantity = contentModel.Seats[i].Quantity;
                reservation.Seats[i].Quantity = quantity;
            }

            return reservation;
        }

        private OrderDTO WaitUntilBooked(Registration registration)
        {
            var deadline = DateTime.Now.AddSeconds(WaitTimeoutInSeconds);

            while (DateTime.Now < deadline)
            {
                var repo = this.repositoryFactory();
                using (repo as IDisposable)
                {
                    var orderDTO = repo.Find<OrderDTO>(registration.Id);

                    if (orderDTO != null && orderDTO.State != "Created")
                    {
                        return orderDTO;
                    }
                }

                Thread.Sleep(500);
            }

            return null;
        }
    }
}
