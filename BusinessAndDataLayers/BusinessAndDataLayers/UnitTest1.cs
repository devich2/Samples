using BusinessLayerLib;
using DomainLib;
using EntityFrameworkCoreGetSQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using RepoDb;
using RepoDbLayer;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace BusinessAndDataLayers
{
    [TestClass]
    public partial class UnitTest1
    {
        #region Fields
        Mock<IRepository> _mockDataLayer;
        BusinessLayer _businessLayer;
        Person _bob = new Person { Key = new Guid("087aca6b-61d4-4d94-8425-1bdfb34dab38"), Name = "Bob" };
        string _id = Guid.NewGuid().ToString().Replace("-", "");
        private bool _customDeleting = false;
        private bool _customDeleted = false;
        private bool _customBefore = false;
        private bool _customAfter = false;
        #endregion

        #region Tests

        [TestMethod]
        public async Task TestGetEntityFramework()
        {
            using (var ordersDbContext = new OrdersDbContext())
            {
                IRepository entityFrameworkDataLayer = new EntityFrameworkDataLayer(ordersDbContext);
                var asyncEnumerable = await entityFrameworkDataLayer
                    .GetAsync<OrderRecord>(o => o.Id == _id);
                var returnValue = await asyncEnumerable.ToListAsync();
                Assert.AreEqual(1, returnValue.Count);
            }
        }

        [TestMethod]
        public async Task TestGetRepoDb()
        {
            SqLiteBootstrap.Initialize();

            using (var connection = new SQLiteConnection(OrdersDbContext.ConnectionString))
            {
                IRepository repoDbDataLayer = new RepoDbDataLayer(connection);
                var asyncEnumerable = await repoDbDataLayer
                    .GetAsync<OrderRecord>(o => o.Id == _id);


                var returnValue = await asyncEnumerable.ToListAsync();
                Assert.AreEqual(1, returnValue.Count);
            }
        }

        [TestMethod]
        public async Task TestGetWithBusinessLayer()
        {
            SqLiteBootstrap.Initialize();

            using (var connection = new SQLiteConnection(OrdersDbContext.ConnectionString))
            {
                var repoDbDataLayer = new RepoDbDataLayer(connection);

                var businessLayer = new BusinessLayer(
                    repoDbDataLayer,
                    beforeGet: async (t, e) => { _customBefore = true; },
                    afterGet: async (t, result) => { _customAfter = true; });

                var asyncEnumerable = await businessLayer
                    .GetAsync<OrderRecord>(o => o.Id == _id);


                var returnValue = await asyncEnumerable.ToListAsync();
                Assert.AreEqual(1, returnValue.Count);
                Assert.IsTrue(_customBefore && _customAfter);
            }
        }


        [TestMethod]
        public async Task TestUpdating()
        {
            //Arrange

            //Return 1 person
            _mockDataLayer.Setup(r => r.GetAsync(It.IsAny<Expression<Func<Person, bool>>>())).Returns(Task.FromResult<IAsyncEnumerable<Person>>(new DummyPersonAsObjectAsyncEnumerable(true)));

            //Act
            var savedPerson = await _businessLayer.SaveAsync(_bob);

            //Assert

            //Verify custom business logic
            Assert.AreEqual("BobUpdatingUpdated", savedPerson.Name);

            //Verify update was called
            _mockDataLayer.Verify(d => d.UpdateAsync(It.IsAny<Person>()), Times.Once);
        }

        [TestMethod]
        public async Task TestInserted()
        {
            //Arrange

            //Return no people
            _mockDataLayer.Setup(r => r.GetAsync<Person>(It.IsAny<Expression<Func<Person, bool>>>())).Returns(Task.FromResult<IAsyncEnumerable<Person>>(new DummyPersonAsObjectAsyncEnumerable(false)));

            //Act
            var savedPerson = await _businessLayer.SaveAsync(_bob);

            //Assert

            //Verify custom business logic
            Assert.AreEqual("BobInsertingInserted", savedPerson.Name);

            //Verify insert was called
            _mockDataLayer.Verify(d => d.InsertAsync(It.IsAny<Person>()), Times.Once);
        }

        [TestMethod]
        public async Task TestDeleted()
        {
            //Act
            await _businessLayer.DeleteAsync<Person>(_bob.Key);

            //Verify insert was called
            _mockDataLayer.Verify(d => d.DeleteAsync(typeof(Person), _bob.Key), Times.Once);

            Assert.IsTrue(_customDeleted && _customDeleting);
        }

        [TestMethod]
        public async Task TestGet()
        {
            //Act
            var people = await _businessLayer.GetAllAsync<Person>();

            //Verify insert was called
            _mockDataLayer.Verify(d => d.GetAsync(It.IsAny<Expression<Func<Person, bool>>>()), Times.Once);

            Assert.IsTrue(_customBefore && _customAfter);
        }


        #endregion

        #region Arrange
        [TestInitialize]
        public async Task TestInitialize()
        {
            _mockDataLayer = new Mock<IRepository>();
            _mockDataLayer.Setup(r => r.UpdateAsync(It.IsAny<object>())).Returns(Task.FromResult<object>(_bob));
            _mockDataLayer.Setup(r => r.InsertAsync(It.IsAny<object>())).Returns(Task.FromResult<object>(_bob));

            _businessLayer = GetBusinessLayer(_mockDataLayer.Object).businessLayer;

            using (var ordersDbContext = new OrdersDbContext())
            {
                ordersDbContext.OrderRecord.Add(new OrderRecord { Id = _id });
                await ordersDbContext.SaveChangesAsync();
            }

        }

        private (BusinessLayer businessLayer, ServiceProvider serviceProvider) GetBusinessLayer(IRepository repository)
        {
            var serviceCollection = new ServiceCollection();

            serviceCollection.AddSingleton<Inserting<Person>>(async (p) =>
            {
                p.Name += "Inserting";
            })
            .AddSingleton<Updating<Person>>(async (p) =>
            {
                p.Name += "Updating";
            })
            .AddSingleton<Updated<Person>>(async (p) =>
            {
                p.Name += "Updated";
            })
            .AddSingleton<Inserted<Person>>(async (p) =>
            {
                p.Name += "Inserted";
            })
            .AddSingleton<Deleting<Person>>(async (key) =>
            {
                _customDeleting = key == _bob.Key;
            })
            .AddSingleton<Deleted<Person>>(async (key) =>
            {
                _customDeleted = key == _bob.Key;
            })
            .AddSingleton<BeforeGet<Person>>(async (query) =>
            {
                _customBefore = true;
            })
            .AddSingleton<AfterGet<Person>>(async (people) =>
            {
                _customAfter = true;
            });

            var serviceProvider = serviceCollection.BuildServiceProvider();

            var businessLayer = new BusinessLayer(
                repository,
                async (type, key) =>
                {
                    var delegateType = typeof(Deleting<>).MakeGenericType(new Type[] { type });
                    var @delegate = (Delegate)serviceProvider.GetService(delegateType);
                    if (@delegate == null) return;
                    await (Task)@delegate?.DynamicInvoke(new object[] { key });
                },
                async (type, key) =>
                {
                    var delegateType = typeof(Deleted<>).MakeGenericType(new Type[] { type });
                    var @delegate = (Delegate)serviceProvider.GetService(delegateType);
                    if (@delegate == null) return;
                    await (Task)@delegate?.DynamicInvoke(new object[] { key });
                }, async (entity) =>
                {
                    var delegateType = typeof(Inserting<>).MakeGenericType(new Type[] { entity.GetType() });
                    var @delegate = (Delegate)serviceProvider.GetService(delegateType);
                    if (@delegate == null) return;
                    await (Task)@delegate?.DynamicInvoke(new object[] { entity });
                },
                async (entity) =>
                {
                    var delegateType = typeof(Inserted<>).MakeGenericType(new Type[] { entity.GetType() });
                    var @delegate = (Delegate)serviceProvider.GetService(delegateType);
                    if (@delegate == null) return;
                    await (Task)@delegate?.DynamicInvoke(new object[] { entity });
                },
                async (entity) =>
                {
                    var delegateType = typeof(Updating<>).MakeGenericType(new Type[] { entity.GetType() });
                    var @delegate = (Delegate)serviceProvider.GetService(delegateType);
                    if (@delegate == null) return;
                    await (Task)@delegate?.DynamicInvoke(new object[] { entity });
                },
                async (entity) =>
                {
                    var delegateType = typeof(Updated<>).MakeGenericType(new Type[] { entity.GetType() });
                    var @delegate = (Delegate)serviceProvider.GetService(delegateType);
                    if (@delegate == null) return;
                    await (Task)@delegate?.DynamicInvoke(new object[] { entity });
                },
                async (type, query) =>
                {
                    var delegateType = typeof(BeforeGet<>).MakeGenericType(new Type[] { type });
                    var @delegate = (Delegate)serviceProvider.GetService(delegateType);
                    if (@delegate == null) return;
                    await (Task)@delegate.DynamicInvoke(new object[] { query });
                },
                async (type, items) =>
                {
                    var delegateType = typeof(AfterGet<>).MakeGenericType(new Type[] { type });
                    var @delegate = (Delegate)serviceProvider.GetService(delegateType);
                    if (@delegate == null) return;
                    await (Task)@delegate?.DynamicInvoke(new object[] { items });
                }
                );

            return (businessLayer, serviceProvider);
        }
        #endregion
    }
}
