﻿using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Cik.CoreLibs.Api;
using Cik.CoreLibs.Domain;
using Cik.Services.Magazine.MagazineService.Command;
using Cik.Services.Magazine.MagazineService.Model;
using Cik.Services.Magazine.MagazineService.Repository;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Cik.CoreLibs.Extensions;
using Cik.CoreLibs.ServiceDiscovery;

namespace Cik.Services.Magazine.MagazineService
{
    public class Startup
    {
        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", true, true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", true)
                .AddEnvironmentVariables();
            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            var guestPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireClaim("scope", "data_category_records")
                .Build();

            services.AddAuthorization(options =>
            {
                options.AddPolicy("data_category_records_admin",
                    policyAdmin => { policyAdmin.RequireClaim("role", "data_category_records_admin"); });
                options.AddPolicy("data_category_records_user",
                    policyUser => { policyUser.RequireClaim("role", "data_category_records_user"); });
            });

            // services.AddMvc(options => { options.Filters.Add(new AuthorizeFilter(guestPolicy)); });

            if (!Configuration["RunInMemory"].ToBoolean())
            {
                // Use a PostgreSQL database
                var sqlConnectionString = Configuration["DataAccessPostgreSqlProvider:ConnectionString"];
                services.AddDbContext<MagazineDbContext>(options =>
                    options.UseNpgsql(
                        sqlConnectionString,
                        b => b.MigrationsAssembly("Cik.Services.Magazine.MagazineService")
                        ));
            }
            else
            {
                // Use a InMemory database
                services.AddDbContext<MagazineDbContext>(options =>
                    options.UseInMemoryDatabase()
                    );
            }

            // Add framework services.
            services.AddMvc();
            services.RegisterServiceDiscovery();

            // Autofac container
            var builder = new ContainerBuilder();
            builder.RegisterType<MagazineDbContext>().AsSelf().SingleInstance();
            builder.RegisterType<CategoryRepository>()
                .As<IRepository<Category, Guid>>()
                .InstancePerLifetimeScope();

            //builder.RegisterInstance(new InMemoryBus()).SingleInstance();
            //builder.Register(x => x.Resolve<InMemoryBus>()).As<ICommandHandler>();
            //builder.Register(x => x.Resolve<InMemoryBus>()).As<IDomainEventPublisher>();
            //builder.Register(x => x.Resolve<InMemoryBus>()).As<IHandlerRegistrar>();

            builder.RegisterAssemblyTypes(typeof (IMediator).GetTypeInfo().Assembly).AsImplementedInterfaces();
            builder.RegisterAssemblyTypes(typeof(CreateCategoryCommand).GetTypeInfo().Assembly).AsImplementedInterfaces();
            builder.Register<SingleInstanceFactory>(ctx =>
            {
                var c = ctx.Resolve<IComponentContext>();
                return t => c.Resolve(t);
            });
            builder.Register<MultiInstanceFactory>(ctx =>
            {
                var c = ctx.Resolve<IComponentContext>();
                return t => (IEnumerable<object>) c.Resolve(typeof (IEnumerable<>).MakeGenericType(t));
            });

            // builder.RegisterType<CategoryQueryModelFinder>().AsImplementedInterfaces().InstancePerLifetimeScope();
            builder.RegisterType<SimpleCommandBus>().As<ICommandBus>();

            // builder.RegisterCommandHandlers();
            builder.Populate(services);
            builder.RegisterType<ConsulDiscoveryService>()
                .As<IDiscoveryService>()
                .InstancePerLifetimeScope();
            builder.RegisterType<RestClient>().AsSelf();

            // build up the container
            var container = builder.Build();
            // container.RegisterHandlers(typeof (Startup));
            return container.Resolve<IServiceProvider>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            loggerFactory.AddDebug();

            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap = new Dictionary<string, string>();
            var jwtBearerOptions = new JwtBearerOptions
            {
                Authority = "https://localhost:44307",
                Audience = "https://localhost:44307/resources",
                AutomaticAuthenticate = true,

                // required if you want to return a 403 and not a 401 for forbidden responses
                AutomaticChallenge = true
            };

            // app.UseJwtBearerAuthentication(jwtBearerOptions);

            if (env.IsDevelopment())
            {
                app.UseBrowserLink();

                // TODO: comment out this because the PostgreSQL issue 
                // SeedData.InitializeMagazineDatabaseAsync(app.ApplicationServices).Wait();
            }

            app.UseMvc();
        }
    }
}